namespace MediaManager.Models;

/// <summary>
/// Одна запись в журнале копирований.
/// Хранит время, имя файла, направление и результат операции.
/// </summary>
public class CopyLogEntry
{
    /// <summary>Время операции</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Имя файла (короткое, без пути)</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Направление копирования (Site2, Эфир, Кодер и т.д.)</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Результат: Success, Error, Cancelled</summary>
    public CopyLogStatus Status { get; set; }

    /// <summary>Время в формате ЧЧ:ММ:СС</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>Иконка статуса для отображения</summary>
    public string StatusIcon => Status switch
    {
        CopyLogStatus.Success => "✅",
        CopyLogStatus.Error => "❌",
        CopyLogStatus.Cancelled => "⛔",
        _ => "❓"
    };

    /// <summary>Цвет статуса (hex) для отображения</summary>
    public string StatusColor => Status switch
    {
        CopyLogStatus.Success => "#2E7D32",
        CopyLogStatus.Error => "#C62828",
        CopyLogStatus.Cancelled => "#E65100",
        _ => "#757575"
    };

    /// <summary>Краткое описание для отображения в одну строку</summary>
    public string Summary => $"{FileName}  →  {Destination}";
}

/// <summary>
/// Статус записи в журнале копирований.
/// </summary>
public enum CopyLogStatus
{
    Success,
    Error,
    Cancelled
}
