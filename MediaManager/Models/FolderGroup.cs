using System.Collections.ObjectModel;

namespace MediaManager.Models;

/// <summary>
/// Группа файлов из одной папки.
/// Используется для отображения файлов с заголовком-папкой.
/// </summary>
public class FolderGroup
{
    /// <summary>Имя папки (отображается как заголовок группы)</summary>
    public string FolderName { get; set; } = string.Empty;

    /// <summary>Полный путь к папке</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Список найденных медиафайлов в этой папке</summary>
    public ObservableCollection<MediaFile> Files { get; set; } = new();
}
