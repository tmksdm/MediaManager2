using System.IO;
using System.Text.RegularExpressions;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Разбирает имя .mp4 файла и определяет его тип и дату.
/// 
/// Поддерживаемые шаблоны имён:
///   ПАНОРАМА_ДАЙДЖЕСТ_00_MM_DD_...mp4   → Дайджест
///   ПАНОРАМА_HH_MM_DD_...mp4            → Панорама (HH = 18, 20 и т.д.)
///   НОВОСТИ_MMDD_...mp4                 → Новости
///   MM_DD_...mp4                        → Архив
/// 
/// DisplayName — просто имя файла без расширения, как есть.
/// </summary>
public static class FileNameParser
{
    // ====================================================================
    // Регулярные выражения — шаблоны для определения типа и даты.
    // Нас интересуют только префикс (тип) и дата (месяц, день).
    // Всё остальное в имени файла мы не трогаем.
    // ====================================================================

    /// <summary>
    /// ДАЙДЖЕСТ: ПАНОРАМА_ДАЙДЖЕСТ_00_MM_DD_...
    /// Проверяем раньше ПАНОРАМЫ, т.к. тоже начинается с "ПАНОРАМА_"
    /// </summary>
    private static readonly Regex DigestRegex = new(
        @"^ПАНОРАМА_ДАЙДЖЕСТ_\d{2}_(?<month>\d{2})_(?<day>\d{2})_",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// ПАНОРАМА: ПАНОРАМА_HH_MM_DD_...
    /// HH — час выпуска (18, 20 и т.д.), нас не интересует для определения типа
    /// </summary>
    private static readonly Regex PanoramaRegex = new(
        @"^ПАНОРАМА_\d{2}_(?<month>\d{2})_(?<day>\d{2})_",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// НОВОСТИ: НОВОСТИ_MMDD_...
    /// Месяц и день слитно, без подчёркивания
    /// </summary>
    private static readonly Regex NewsRegex = new(
        @"^НОВОСТИ_(?<month>\d{2})(?<day>\d{2})_",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// АРХИВ: MM_DD_...
    /// Просто две пары цифр в начале
    /// </summary>
    private static readonly Regex ArchiveRegex = new(
        @"^(?<month>\d{2})_(?<day>\d{2})_",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Попытаться разобрать имя файла.
    /// Возвращает заполненный MediaFile или null, если файл не подходит ни под один шаблон.
    /// </summary>
    /// <param name="fullPath">Полный путь к файлу</param>
    /// <param name="selectedYear">Год из выбранной даты (для восстановления полной даты)</param>
    /// <returns>MediaFile или null</returns>
    public static MediaFile? TryParse(string fullPath, int selectedYear)
    {
        string fileName = Path.GetFileName(fullPath);

        // Пробуем каждый шаблон по очереди.
        // Порядок важен: ДАЙДЖЕСТ проверяем до ПАНОРАМЫ!

        // --- Попытка 1: ДАЙДЖЕСТ ---
        Match match = DigestRegex.Match(fileName);
        if (match.Success)
        {
            return CreateMediaFile(fullPath, fileName, match, MediaFileType.Digest, selectedYear);
        }

        // --- Попытка 2: ПАНОРАМА ---
        match = PanoramaRegex.Match(fileName);
        if (match.Success)
        {
            return CreateMediaFile(fullPath, fileName, match, MediaFileType.Panorama, selectedYear);
        }

        // --- Попытка 3: НОВОСТИ ---
        match = NewsRegex.Match(fileName);
        if (match.Success)
        {
            return CreateMediaFile(fullPath, fileName, match, MediaFileType.News, selectedYear);
        }

        // --- Попытка 4: АРХИВ ---
        match = ArchiveRegex.Match(fileName);
        if (match.Success)
        {
            return CreateMediaFile(fullPath, fileName, match, MediaFileType.Archive, selectedYear);
        }

        // Ни один шаблон не подошёл
        return null;
    }

    /// <summary>
    /// Создать объект MediaFile из результата regex-разбора.
    /// </summary>
    private static MediaFile? CreateMediaFile(
        string fullPath, string fileName, Match match,
        MediaFileType fileType, int selectedYear)
    {
        // Извлекаем месяц и день из именованных групп regex
        if (!int.TryParse(match.Groups["month"].Value, out int month) ||
            !int.TryParse(match.Groups["day"].Value, out int day))
        {
            return null;
        }

        // Проверяем, что месяц и день в допустимых диапазонах
        if (month < 1 || month > 12 || day < 1 || day > 31)
        {
            return null;
        }

        // Собираем полную дату
        DateTime fileDate;
        try
        {
            fileDate = new DateTime(selectedYear, month, day);
        }
        catch
        {
            return null; // Невалидная дата (например, 30 февраля)
        }

        // DisplayName — просто имя файла без расширения .mp4
        string displayName = Path.GetFileNameWithoutExtension(fileName);

        // Получаем размер файла
        long fileSize = 0;
        try
        {
            var fileInfo = new FileInfo(fullPath);
            fileSize = fileInfo.Length;
        }
        catch
        {
            // Файл может быть недоступен
        }

        // Информация о родительской папке
        string? parentPath = Path.GetDirectoryName(fullPath);
        string parentName = parentPath != null
            ? new DirectoryInfo(parentPath).Name
            : string.Empty;

        return new MediaFile
        {
            FullPath = fullPath,
            FileName = fileName,
            FileType = fileType,
            FileDate = fileDate,
            DisplayName = displayName,
            FileSize = fileSize,
            ParentFolderName = parentName,
            ParentFolderPath = parentPath ?? string.Empty
        };
    }
}
