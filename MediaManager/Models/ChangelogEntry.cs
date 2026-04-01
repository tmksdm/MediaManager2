namespace MediaManager.Models;

/// <summary>
/// Одна запись в истории изменений (changelog).
/// </summary>
public class ChangelogEntry
{
    /// <summary>Версия в формате YYMMDD или YYMMDD.N</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Дата релиза</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Является ли эта версия текущей (самой новой)</summary>
    public bool IsCurrent { get; set; }

    /// <summary>Список изменений в этой версии</summary>
    public List<string> Changes { get; set; } = new();

    /// <summary>Отображение версии с приклеенной буквой v: «v260401»</summary>
    public string VersionDisplay => "v" + Version;
}
