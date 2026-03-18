using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Строит пути назначения для копирования и выполняет само копирование.
/// 
/// Логика путей:
///   ПАНОРАМА / ДАЙДЖЕСТ → Site2 (архив), Эфир, Кодер Site
///   НОВОСТИ             → Хранилище новостей, Эфир 25к, Кодер 25к
///   АРХИВ               → Сюжеты панорамы
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
    /// Для ПАНОРАМА / ДАЙДЖЕСТ — efirTime используется в формировании имени в Эфир-папке.
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
                    // 1) Site2 архив: \\ARCHIVE2\Site2\{year}\{MM}_{MonthTitle}\{DD}\filename
                    string site2Dir = Path.Combine(settings.Site2Archive, year, $"{mm}_{monthTitle}", dd);
                    destinations.Add(new CopyDestination
                    {
                        Label = "Site2 (архив)",
                        DestinationPath = Path.Combine(site2Dir, file.FileName)
                    });

                    // 2) Эфир: \\Archive1\export\ПАНОРАМА\filename
                    //    Если выбрано время эфира — подставляем его в имя файла
                    string efirFileName = file.FileName;
                    if (!string.IsNullOrEmpty(efirTime))
                    {
                        efirFileName = ReplaceTimeInFileName(file, efirTime);
                    }
                    destinations.Add(new CopyDestination
                    {
                        Label = "Эфир",
                        DestinationPath = Path.Combine(settings.EfirPanorama, efirFileName)
                    });

                    // 3) Кодер Site: \\NEWS-ENCODER\Coder_SITE\filename
                    destinations.Add(new CopyDestination
                    {
                        Label = "Кодер Site",
                        DestinationPath = Path.Combine(settings.CoderSite, file.FileName)
                    });
                    break;
                }

            case MediaFileType.News:
                {
                    // 1) Хранилище: \\archive2\Архив_25К\НОВОСТИ\{year}\{MM}_{month_lower}\filename
                    string newsDir = Path.Combine(settings.NewsStorage, year, $"{mm}_{monthLower}");
                    destinations.Add(new CopyDestination
                    {
                        Label = "Хранилище",
                        DestinationPath = Path.Combine(newsDir, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 2) Эфир 25к: \\Archive2\25k\25k\НОВОСТИ\filename
                    destinations.Add(new CopyDestination
                    {
                        Label = "Эфир 25к",
                        DestinationPath = Path.Combine(settings.NewsEfir25, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 3) Кодер 25к: \\News-encoder25k\25K\filename
                    destinations.Add(new CopyDestination
                    {
                        Label = "Кодер 25к",
                        DestinationPath = Path.Combine(settings.Coder25, file.FileName),
                        CopyPathToClipboard = true
                    });
                    break;
                }

            case MediaFileType.Archive:
                {
                    // 1) Сюжеты панорамы: \\Archive2\сюжеты панорамы 2\{year}\{MM}_{MONTH_UPPER}\filename
                    string archDir = Path.Combine(settings.ArchiveStories, year, $"{mm}_{monthUpper}");
                    destinations.Add(new CopyDestination
                    {
                        Label = "Сюжеты",
                        DestinationPath = Path.Combine(archDir, file.FileName)
                    });
                    break;
                }
        }

        return destinations;
    }

    /// <summary>
    /// Проверяет, скопирован ли файл в указанное место
    /// (совпадает размер и время модификации с точностью до 2 секунд).
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
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Копирует файл с отчётом о прогрессе. Создаёт папки назначения если их нет.
    /// Возвращает true если копирование прошло успешно.
    /// </summary>
    public async Task<bool> CopyFileAsync(
        string sourcePath,
        string destPath,
        IProgress<double>? progress = null,
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

            using var sourceStream = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize, useAsync: true);

            using var destStream = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize, useAsync: true);

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(
                       buffer.AsMemory(0, bufferSize), cancellationToken)) > 0)
            {
                await destStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead), cancellationToken);

                copiedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report((double)copiedBytes / totalBytes * 100.0);
                }
            }

            // Устанавливаем время модификации как у оригинала
            File.SetLastWriteTime(destPath, sourceInfo.LastWriteTime);

            progress?.Report(100.0);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Удаляем недокопированный файл
            try { File.Delete(destPath); } catch { }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Заменяет время (часы) в имени файла ПАНОРАМЫ / ДАЙДЖЕСТА.
    /// ПАНОРАМА_18_03_18_сюжет.mp4  →  ПАНОРАМА_20_03_18_сюжет.mp4
    /// ПАНОРАМА_ДАЙДЖЕСТ_00_03_18_сюжет.mp4  →  ПАНОРАМА_ДАЙДЖЕСТ_07_03_18_сюжет.mp4
    /// </summary>
    private static string ReplaceTimeInFileName(MediaFile file, string newTime)
    {
        string name = file.FileName;
        string timeFormatted = newTime.PadLeft(2, '0');

        if (file.FileType == MediaFileType.Digest)
        {
            // ПАНОРАМА_ДАЙДЖЕСТ_XX_...  — заменяем XX (позиция после второго _)
            // Формат: ПАНОРАМА_ДАЙДЖЕСТ_00_MM_DD_...
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
            // ПАНОРАМА_XX_MM_DD_... — заменяем XX (позиция после первого _)
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
