using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Строит пути назначения для копирования и выполняет само копирование.
/// 
/// Основное копирование — через robocopy.exe с прогрессом.
/// После копирования на сетевой ресурс отправляется SHChangeNotify —
/// уведомление оболочки Windows о появлении нового файла.
/// Это имитирует поведение Проводника и решает проблему с AME.
/// 
/// Если SHChangeNotify не поможет — есть fallback через SHFileOperation
/// (полный Shell API копирования, идентичный Ctrl+C / Ctrl+V в Проводнике).
/// </summary>
public class FileCopyService
{
    // ========================= Shell API =========================

    /// <summary>
    /// Уведомляет оболочку Windows о событии файловой системы.
    /// Именно это делает Проводник после копирования файла.
    /// AME может слушать эти уведомления для подхвата файлов.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        int wEventId,
        uint uFlags,
        [MarshalAs(UnmanagedType.LPWStr)] string? dwItem1,
        [MarshalAs(UnmanagedType.LPWStr)] string? dwItem2);

    // Константы для SHChangeNotify
    private const int SHCNE_CREATE = 0x00000002;      // Файл создан
    private const int SHCNE_UPDATEDIR = 0x00001000;    // Содержимое папки изменилось
    private const uint SHCNF_PATH = 0x0005;            // dwItem — путь к файлу/папке

    /// <summary>
    /// SHFileOperation — Shell API копирование файлов.
    /// Это ИМЕННО тот механизм, который использует Проводник Windows
    /// при Ctrl+C → Ctrl+V. Включает Shell Notifications, 
    /// корректные SMB-флаги и все прочие особенности explorer.exe.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszProgressTitle;
    }

    private const uint FO_COPY = 0x0002;
    private const ushort FOF_SILENT = 0x0004;           // Без диалога прогресса
    private const ushort FOF_NOCONFIRMATION = 0x0010;   // Без подтверждений
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;   // Без подтверждения создания папок
    private const ushort FOF_NOERRORUI = 0x0400;        // Без диалогов ошибок

    // ========================= Названия месяцев =========================

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
    /// Копирует файл используя комбинированный подход:
    /// 
    /// 1. robocopy.exe — для основного копирования с прогрессом
    /// 2. SHChangeNotify — уведомление Shell о новом файле
    /// 
    /// Если файл копируется на UNC-путь (сетевой ресурс),
    /// после robocopy отправляются Shell-уведомления SHCNE_CREATE
    /// и SHCNE_UPDATEDIR — те же, что Проводник отправляет после
    /// Ctrl+C → Ctrl+V. AME может слушать именно их.
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

            bool needsRename = !string.Equals(sourceFileName, destFileName, StringComparison.OrdinalIgnoreCase);

            var sourceInfo = new FileInfo(sourcePath);
            long totalBytes = sourceInfo.Length;
            var copyStart = DateTime.UtcNow;
            var lastReport = DateTime.UtcNow;
            const int reportIntervalMs = 50;

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

            using var ctReg = cancellationToken.Register(() =>
            {
                try { process?.Kill(); } catch { }
            });

            var stderrTask = Task.Run(async () =>
            {
                string? errLine;
                while ((errLine = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                {
                    stderrBuilder.AppendLine(errLine);
                }
            }, cancellationToken);

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

            if (exitCode >= 8)
            {
                string stderr = stderrBuilder.ToString().Trim();
                LogService.Error(
                    $"robocopy ошибка (код {exitCode}): {sourcePath} → {destPath}" +
                    (string.IsNullOrEmpty(stderr) ? "" : $"\nstderr: {stderr}"));
                return false;
            }

            // Финальный путь файла
            string finalPath = destPath;

            if (needsRename)
            {
                string copiedPath = Path.Combine(destDir!, sourceFileName);
                finalPath = Path.Combine(destDir!, destFileName);

                if (File.Exists(copiedPath))
                {
                    if (File.Exists(finalPath))
                    {
                        File.Delete(finalPath);
                    }
                    File.Move(copiedPath, finalPath);
                }
            }

            // Отправляем Shell-уведомления — имитируем поведение Проводника.
            // Это может быть ключевым отличием: AME слушает Shell Notifications,
            // а не ReadDirectoryChangesW. Без этих уведомлений AME не «видит»
            // файл корректно, хотя он физически на диске.
            NotifyShell(finalPath, destDir!);

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
    /// Копирует файл через Shell API (SHFileOperation) — ТОЧНО как Проводник.
    /// Это fallback-метод: без прогресса, но гарантированно идентичен Ctrl+C → Ctrl+V.
    /// Вызывать только если robocopy + SHChangeNotify не решили проблему.
    /// 
    /// ВАЖНО: SHFileOperation должен вызываться из STA-потока (UI или специальный).
    /// </summary>
    public bool CopyFileShell(string sourcePath, string destPath)
    {
        try
        {
            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // SHFileOperation требует double-null-terminated строки
            var shFileOp = new SHFILEOPSTRUCT
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_COPY,
                pFrom = sourcePath + '\0' + '\0',
                pTo = destPath + '\0' + '\0',
                fFlags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = ""
            };

            int result = SHFileOperation(ref shFileOp);

            if (result != 0)
            {
                LogService.Error($"SHFileOperation ошибка (код {result}): {sourcePath} → {destPath}");
                return false;
            }

            if (shFileOp.fAnyOperationsAborted)
            {
                LogService.Error($"SHFileOperation прервана: {sourcePath} → {destPath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"SHFileOperation исключение: {sourcePath} → {destPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Отправляет Shell-уведомления о создании файла и обновлении папки.
    /// Это имитирует то, что делает Проводник Windows после копирования.
    /// </summary>
    private static void NotifyShell(string filePath, string folderPath)
    {
        try
        {
            // Уведомление: «файл создан»
            SHChangeNotify(SHCNE_CREATE, SHCNF_PATH, filePath, null);

            // Уведомление: «содержимое папки изменилось»
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATH, folderPath, null);
        }
        catch (Exception ex)
        {
            // Не критично — логируем и продолжаем
            LogService.Error($"SHChangeNotify ошибка: {filePath}", ex);
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
