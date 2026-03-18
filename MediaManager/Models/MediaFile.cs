namespace MediaManager.Models;

/// <summary>
/// Тип медиафайла, определяется по префиксу имени файла.
/// </summary>
public enum MediaFileType
{
    /// <summary>ПАНОРАМА — основной выпуск</summary>
    Panorama,

    /// <summary>ДАЙДЖЕСТ — краткий выпуск</summary>
    Digest,

    /// <summary>НОВОСТИ — новостной сюжет</summary>
    News,

    /// <summary>АРХИВ — архивный сюжет</summary>
    Archive,

    /// <summary>Неизвестный тип — файл не подходит под шаблоны</summary>
    Unknown
}

/// <summary>
/// Один найденный медиафайл.
/// Хранит всю информацию: путь, тип, дату, отображаемое имя.
/// </summary>
public class MediaFile
{
    /// <summary>Полный путь к файлу на диске</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Имя файла (без пути, например "ПАНОРАМА_18_03_18_Сюжет.mp4")</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Тип файла (определён по префиксу)</summary>
    public MediaFileType FileType { get; set; } = MediaFileType.Unknown;

    /// <summary>Дата из имени файла</summary>
    public DateTime FileDate { get; set; }

    /// <summary>
    /// "Чистое" имя для отображения — без префикса и даты.
    /// Например, для "ПАНОРАМА_18_03_18_Сюжет_про_погоду.mp4" → "Сюжет про погоду"
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Название типа для отображения на бейдже.
    /// </summary>
    public string TypeLabel => FileType switch
    {
        MediaFileType.Panorama => "ПАНОРАМА",
        MediaFileType.Digest => "ДАЙДЖЕСТ",
        MediaFileType.News => "НОВОСТИ",
        MediaFileType.Archive => "АРХИВ",
        _ => "???"
    };

    /// <summary>
    /// Цвет бейджа в зависимости от типа (hex-строка).
    /// </summary>
    public string TypeColor => FileType switch
    {
        MediaFileType.Panorama => "#8E24AA",
        MediaFileType.Digest => "#8E24AA",
        MediaFileType.News => "#E53935",
        MediaFileType.Archive => "#43A047",
        _ => "#9E9E9E"
    };

    /// <summary>Размер файла в байтах</summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Размер файла, отформатированный для отображения (например, "1.5 ГБ").
    /// </summary>
    public string FileSizeText
    {
        get
        {
            if (FileSize < 1024)
                return $"{FileSize} Б";
            if (FileSize < 1024 * 1024)
                return $"{FileSize / 1024.0:F1} КБ";
            if (FileSize < 1024L * 1024 * 1024)
                return $"{FileSize / (1024.0 * 1024):F1} МБ";
            return $"{FileSize / (1024.0 * 1024 * 1024):F2} ГБ";
        }
    }

    /// <summary>Имя родительской папки (для группировки)</summary>
    public string ParentFolderName { get; set; } = string.Empty;

    /// <summary>Полный путь к родительской папке</summary>
    public string ParentFolderPath { get; set; } = string.Empty;
}
