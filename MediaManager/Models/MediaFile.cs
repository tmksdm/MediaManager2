using System.ComponentModel;
using System.Runtime.CompilerServices;

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
/// Хранит всю информацию: путь, тип, дату, отображаемое имя, статус копирования.
/// Реализует INotifyPropertyChanged, чтобы кнопки обновлялись при изменении статуса.
/// </summary>
public class MediaFile : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

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
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Название типа для отображения на бейдже.</summary>
    public string TypeLabel => FileType switch
    {
        MediaFileType.Panorama => "ПАНОРАМА",
        MediaFileType.Digest => "ДАЙДЖЕСТ",
        MediaFileType.News => "НОВОСТИ",
        MediaFileType.Archive => "АРХИВ",
        _ => "???"
    };

    /// <summary>Цвет бейджа в зависимости от типа (hex-строка).</summary>
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

    /// <summary>Размер файла, отформатированный для отображения.</summary>
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

    // === Статусы копирования по направлениям ===

    // ПАНОРАМА / ДАЙДЖЕСТ
    private bool _isCopiedToSite2;
    public bool IsCopiedToSite2
    {
        get => _isCopiedToSite2;
        set { if (_isCopiedToSite2 != value) { _isCopiedToSite2 = value; OnPropertyChanged(); } }
    }

    private bool _isCopiedToEfir;
    public bool IsCopiedToEfir
    {
        get => _isCopiedToEfir;
        set { if (_isCopiedToEfir != value) { _isCopiedToEfir = value; OnPropertyChanged(); } }
    }

    private bool _isCopiedToCoder;
    public bool IsCopiedToCoder
    {
        get => _isCopiedToCoder;
        set { if (_isCopiedToCoder != value) { _isCopiedToCoder = value; OnPropertyChanged(); } }
    }

    // НОВОСТИ
    private bool _isCopiedToStorage;
    public bool IsCopiedToStorage
    {
        get => _isCopiedToStorage;
        set { if (_isCopiedToStorage != value) { _isCopiedToStorage = value; OnPropertyChanged(); } }
    }

    private bool _isCopiedToEfir25;
    public bool IsCopiedToEfir25
    {
        get => _isCopiedToEfir25;
        set { if (_isCopiedToEfir25 != value) { _isCopiedToEfir25 = value; OnPropertyChanged(); } }
    }

    private bool _isCopiedToCoder25;
    public bool IsCopiedToCoder25
    {
        get => _isCopiedToCoder25;
        set { if (_isCopiedToCoder25 != value) { _isCopiedToCoder25 = value; OnPropertyChanged(); } }
    }

    // АРХИВ
    private bool _isCopiedToArchive;
    public bool IsCopiedToArchive
    {
        get => _isCopiedToArchive;
        set { if (_isCopiedToArchive != value) { _isCopiedToArchive = value; OnPropertyChanged(); } }
    }
}
