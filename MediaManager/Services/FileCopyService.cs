using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Строит пути назначения для копирования и выполняет само копирование.
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
                    // 1) Site2 архив
                    string site2Dir = Path.Combine(settings.Site2Archive, year, $"{mm}_{monthTitle}", dd);
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Site2,
                        DestinationPath = Path.Combine(site2Dir, file.FileName)
                    });

                    // 2) Эфир
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

                    // 3) Кодер Site
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.CoderSite,
                        DestinationPath = Path.Combine(settings.CoderSite, file.FileName)
                    });
                    break;
                }

            case MediaFileType.News:
                {
                    // 1) Хранилище
                    string newsDir = Path.Combine(settings.NewsStorage, year, $"{mm}_{monthLower}");
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Storage,
                        DestinationPath = Path.Combine(newsDir, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 2) Эфир 25к
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Efir25,
                        DestinationPath = Path.Combine(settings.NewsEfir25, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 3) Кодер 25к
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
                    // 1) Сюжеты панорамы
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
    /// Копирует файл с отчётом о прогрессе, скорости и оставшемся времени.
    /// Создаёт папки назначения если их нет.
    /// Прогресс сообщается не чаще 20 раз в секунду, чтобы не забивать UI-поток
    /// и не блокировать обработку кликов (например, кнопки «Отмена»).
    /// </summary>
    public async Task<bool> CopyFileAsync(
        string sourcePath,
        string destPath,
        IProgress<CopyProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            const int bufferSize = 1024 * 1024; // 1 МБ буфер
            var buffer = new byte[bufferSize];

            var sourceInfo = new FileInfo(sourcePath);
            long totalBytes = sourceInfo.Length;
            long copiedBytes = 0;

            // Таймер для throttle прогресса: не чаще 20 раз в секунду (50 мс).
            // Без этого Progress<T>.Report() забивает UI-поток сотнями сообщений,
            // и клики по кнопке «Отмена» не успевают обработаться.
            var lastReport = DateTime.UtcNow;
            const int reportIntervalMs = 50;

            // Момент начала копирования — для расчёта средней скорости
            var copyStart = DateTime.UtcNow;

            // Копируем содержимое файла
            using (var sourceStream = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize, useAsync: true))
            using (var destStream = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize, useAsync: true))
            {
                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(
                           buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
                {
                    await destStream.WriteAsync(
                        buffer.AsMemory(0, bytesRead), cancellationToken);

                    copiedBytes += bytesRead;

                    // Сообщаем прогресс не чаще чем раз в 50 мс
                    if (totalBytes > 0 && progress != null)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - lastReport).TotalMilliseconds >= reportIntervalMs)
                        {
                            double elapsedSeconds = (now - copyStart).TotalSeconds;
                            double bytesPerSec = elapsedSeconds > 0.1
                                ? copiedBytes / elapsedSeconds
                                : 0;

                            long remainingBytes = totalBytes - copiedBytes;
                            TimeSpan? remaining = bytesPerSec > 0
                                ? TimeSpan.FromSeconds(remainingBytes / bytesPerSec)
                                : null;

                            progress.Report(new CopyProgressInfo
                            {
                                Percent = (double)copiedBytes / totalBytes * 100.0,
                                BytesPerSecond = bytesPerSec,
                                Remaining = remaining
                            });

                            lastReport = now;
                        }
                    }
                }
            }
            // Потоки закрыты — теперь безопасно ставить время

            // Копируем и время модификации, и время создания
            File.SetLastWriteTime(destPath, sourceInfo.LastWriteTime);
            File.SetCreationTime(destPath, sourceInfo.CreationTime);

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
            // Удаляем недокопированный файл
            try { File.Delete(destPath); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            LogService.Error($"Ошибка копирования: {sourcePath} → {destPath}", ex);
            return false;
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
