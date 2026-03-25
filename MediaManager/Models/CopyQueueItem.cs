namespace MediaManager.Models;

/// <summary>
/// Одна задача в очереди копирования.
/// Хранит файл, направление и путь назначения.
/// Создаётся при нажатии кнопки копирования,
/// обрабатывается по очереди (FIFO).
/// 
/// Id нужен для точного удаления из очереди —
/// если один файл поставлен в два направления,
/// нельзя удалить по имени, нужен уникальный идентификатор.
/// </summary>
public class CopyQueueItem
{
    /// <summary>Уникальный идентификатор задачи (для удаления из очереди)</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Файл, который нужно скопировать</summary>
    public MediaFile File { get; set; } = null!;

    /// <summary>Ключ направления (Label): «Site2 (архив)», «Эфир» и т.д.</summary>
    public string DestinationKey { get; set; } = string.Empty;

    /// <summary>Полный путь назначения (рассчитан при постановке в очередь)</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Нужно ли копировать путь в буфер обмена после успеха</summary>
    public bool CopyPathToClipboard { get; set; }

    /// <summary>Краткое описание для отображения в панели очереди</summary>
    public string DisplayText => $"{File.FileName}  →  {DestinationKey}";
}
