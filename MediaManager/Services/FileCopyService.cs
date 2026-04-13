using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Строит пути назначения для копирования и выполняет само копирование.
/// 
/// Копирование выполняется через robocopy.exe (встроен в Windows) —
/// это решает проблему с Adobe Media Encoder, который подхватывал
/// недокопированные файлы при потоковом копировании или CopyFileEx.
/// </summary>
public class FileCopyService
{
    // Названия месяцев в разных регистрах для формирования путей
    private static readonly string[] MonthsTitleCase =
        ["Январь","Февраль","Март","Апрель","Май","Июнь",
         "Июль","Август","Сентябрь","Октябрь","Ноябрь","Декабрь"];

    private static readonly string[] MonthsLowerCase =
        ["январь","февраль","март","апрель","май","июнь",
         "июль","август","сентябрь","октябрь","ноябрь","декабрь"];

    private static readonly string[] MonthsUpperCase =
        ["ЯНВАРЬ","ФЕВРАЛЬ","МАРТ","АПРЕЛЬ","МАЙ","ИЮНЬ",
         "ИЮЛЬ","АВГУСТ","СЕНТЯБРЬ","ОКТЯБРЬ","НОЯБРЬ","ДЕКАБРЬ"];

    /// <summary>
    /// Описание одного направления копирования: имя кнопки + путь назначения.
    /// </summary>
    public class CopyDestination
    {
        public string Label { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public bool CopyPathToClipboard { get; set; } = false;
    }

    /// <summary>
    /// Возвращает список направлений копирования для данного файла.
    /// </summary>
    public List<CopyDestination> GetDestinations(
        MediaFile file, AppSettings settings, string? efirTime = null)
    {
        var destinations = new List<CopyDestination>();
        DateTime d = file.FileDate;
        string mm = d.Month.ToString("D2");
        string dd = d.Day.ToString("D2");
        string year = d.Year.ToString();
        string monthTitle = MonthsTitleCase[d.Month - 1];
        string monthLower = MonthsLowerCase[d.Month - 1];
        string monthUpper = MonthsUpperCase[d.Month - 1];

        switch (file.FileType)
        {
            case MediaFileType.Panorama:
            case MediaFileType.Digest:
                {
                    string site2Dir = Path.Combine(settings.Site2Archive, year, $"{mm}_{monthTitle}", dd);
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Site2,
                        DestinationPath = Path.Combine(site2Dir, file.FileName)
                    });

                    string efirFileName = file.FileName;
                    if (!string.IsNullOrEmpty(efirTime))
                    {
                        efirFileName = ReplaceTimeInFileName(file, efirTime);
                    }
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Efir,
                        DestinationPath = Path.Combine(settings.EfirPanorama, efirFileName)
                    });

                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.CoderSite,
                        DestinationPath = Path.Combine(settings.CoderSite, file.FileName)
                    });
                    break;
                }

            case MediaFileType.News:
                {
                    string newsDir = Path.Combine(settings.NewsStorage, year, $"{mm}_{monthLower}");
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Storage,
                        DestinationPath = Path.Combine(newsDir, file.FileName),
                        CopyPathToClipboard = true
                    });

                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Efir25,
                        DestinationPath = Path.Combine(settings.NewsEfir25, file.FileName),
                        CopyPathToClipboard = true
                    });

                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Coder25,
                        DestinationPath = Path.Combine(settings.Coder25, file.FileName),
                        CopyPathToClipboard = true
                    });
                    break;
                }

            case MediaFileType.Archive:
                {
                    string archDir = Path.Combine(settings.ArchiveStories, year, $"{mm}_{monthUpper}");
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Stories,
                        DestinationPath = Path.Combine(archDir, file.FileName)
                    });
                    break;
                }
        }

        return destinations;
    }

    /// <summary>
    /// Проверяет, скопирован ли файл в указанное место.
    /// </summary>
    public bool IsAlreadyCopied(string sourcePath, string destPath)
    {
        try
        {
            if (!File.Exists(destPath))
                return false;

            var srcInfo = new FileInfo(sourcePath);
            var dstInfo = new FileInfo(destPath);

            if (srcInfo.Length != dstInfo.Length)
                return false;

            TimeSpan timeDiff = (srcInfo.LastWriteTime - dstInfo.LastWriteTime).Duration();
            return timeDiff.TotalSeconds <= 2;
        }
        catch (Exception ex)
        {
            LogService.Error($"Ошибка проверки копии: {sourcePath} → {destPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Копирует файл через robocopy.exe — встроенную утилиту Windows.
    /// 
    /// robocopy использует тот же механизм копирования, что и Проводник:
    /// файл блокируется на уровне ОС до завершения, метаданные копируются
    /// атомарно, SMB oplocks обрабатываются корректно.
    /// 
    /// Прогресс парсится из stdout robocopy (строки вида "  12.3%").
    /// Отмена через Process.Kill() при срабатывании CancellationToken.
    /// </summary>
    public async Task<bool> CopyFileAsync(
        string sourcePath,
        string destPath,
        IProgress<CopyProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Process? process = null;

        try
        {
            // Создаём папку назначения если не существует
            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            string sourceDir = Path.GetDirectoryName(sourcePath)!;
            string sourceFileName = Path.GetFileName(sourcePath);
            string destFileName = Path.GetFileName(destPath);

            // Если имя файла назначения отличается (замена времени эфира)
            bool needsRename = !string.Equals(sourceFileName, destFileName, StringComparison.OrdinalIgnoreCase);

            var sourceInfo = new FileInfo(sourcePath);
            long totalBytes = sourceInfo.Length;
            var copyStart = DateTime.UtcNow;
            var lastReport = DateTime.UtcNow;
            const int reportIntervalMs = 50;

            // /COPY:DAT — копировать данные, атрибуты, время
            // /IS — включать одинаковые файлы (перезаписывать)
            // /IT — включать «tweaked» файлы
            // /BYTES — размеры в байтах (для парсинга прогресса)
            // /NJH /NJS — без заголовка и итога
            // /NDL — без имён директорий
            // /NC — без классов файлов
            // /NS — без размеров файлов
            string args = $"\"{sourceDir}\" \"{destDir}\" \"{sourceFileName}\" /COPY:DAT /IS /IT /BYTES /NJH /NJS /NDL /NC /NS";

            var psi = new ProcessStartInfo
            {
                FileName = "robocopy.exe",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process = new Process { StartInfo = psi };

            var stderrBuilder = new StringBuilder();
            var percentRegex = new Regex(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled);

            process.Start();

            // Регистрируем отмену — убиваем процесс
            using var ctReg = cancellationToken.Register(() =>
            {
                try { process?.Kill(); } catch { }
            });

            // Читаем stderr целиком
            var stderrTask = Task.Run(async () =>
            {
                string? errLine;
                while ((errLine = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                {
                    stderrBuilder.AppendLine(errLine);
                }
            }, cancellationToken);

            // Читаем stdout посимвольно для парсинга прогресса
            var stdoutTask = Task.Run(async () =>
            {
                using var reader = process.StandardOutput;
                var lineBuffer = new StringBuilder();
                var charBuffer = new char[1];

                while (true)
                {
                    int read;
                    try
                    {
                        read = await reader.ReadAsync(charBuffer.AsMemory(0, 1), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (read == 0) break;

                    char c = charBuffer[0];

                    if (c == '\r' || c == '\n')
                    {
                        if (lineBuffer.Length > 0)
                        {
                            string line = lineBuffer.ToString().Trim();
                            lineBuffer.Clear();

                            var match = percentRegex.Match(line);
                            if (match.Success && progress != null)
                            {
                                if (double.TryParse(match.Groups[1].Value,
                                    NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out double percent))
                                {
                                    var now = DateTime.UtcNow;
                                    if ((now - lastReport).TotalMilliseconds >= reportIntervalMs
                                        || percent >= 99.9)
                                    {
                                        double elapsedSeconds = (now - copyStart).TotalSeconds;
                                        long estimatedCopied = (long)(totalBytes * percent / 100.0);
                                        double bytesPerSec = elapsedSeconds > 0.1
                                            ? estimatedCopied / elapsedSeconds
                                            : 0;

                                        long remainingBytes = totalBytes - estimatedCopied;
                                        TimeSpan? remaining = bytesPerSec > 0
                                            ? TimeSpan.FromSeconds(remainingBytes / bytesPerSec)
                                            : null;

                                        progress.Report(new CopyProgressInfo
                                        {
                                            Percent = percent,
                                            BytesPerSecond = bytesPerSec,
                                            Remaining = remaining
                                        });

                                        lastReport = now;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        lineBuffer.Append(c);
                    }
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            try { await stdoutTask; } catch (OperationCanceledException) { }
            try { await stderrTask; } catch (OperationCanceledException) { }

            int exitCode = process.ExitCode;

            // robocopy exit codes: 0–7 = успех, 8+ = ошибка
            if (exitCode >= 8)
            {
                string stderr = stderrBuilder.ToString().Trim();
                LogService.Error(
                    $"robocopy ошибка (код {exitCode}): {sourcePath} → {destPath}" +
                    (string.IsNullOrEmpty(stderr) ? "" : $"\nstderr: {stderr}"));
                return false;
            }

            // Если нужно переименование
            if (needsRename)
            {
                string copiedPath = Path.Combine(destDir!, sourceFileName);
                string finalPath = Path.Combine(destDir!, destFileName);

                if (File.Exists(copiedPath))
                {
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }
                    File.Move(copiedPath, finalPath);
                }
            }

            progress?.Report(new CopyProgressInfo
            {
                Percent = 100.0,
                BytesPerSecond = 0,
                Remaining = TimeSpan.Zero
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(destPath); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            try { File.Delete(destPath); } catch { }
            LogService.Error($"Ошибка копирования: {sourcePath} → {destPath}", ex);
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Заменяет время (часы) в имени файла ПАНОРАМЫ / ДАЙДЖЕСТА.
    /// </summary>
    private static string ReplaceTimeInFileName(MediaFile file, string newTime)
    {
        string name = file.FileName;
        string timeFormatted = newTime.PadLeft(2, '0');

        if (file.FileType == MediaFileType.Digest)
        {
            int idx = name.IndexOf("ДАЙДЖЕСТ_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int timeStart = idx + "ДАЙДЖЕСТ_".Length;
                if (timeStart + 2 <= name.Length)
                {
                    name = string.Concat(name.AsSpan(0, timeStart), timeFormatted, name.AsSpan(timeStart + 2));
                }
            }
        }
        else if (file.FileType == MediaFileType.Panorama)
        {
            int idx = name.IndexOf("ПАНОРАМА_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int timeStart = idx + "ПАНОРАМА_".Length;
                if (timeStart + 2 <= name.Length)
                {
                    name = string.Concat(name.AsSpan(0, timeStart), timeFormatted, name.AsSpan(timeStart + 2));
                }
            }
        }

        return name;
    }
}
