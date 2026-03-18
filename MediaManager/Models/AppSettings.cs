namespace MediaManager.Models;

/// <summary>
/// Все настраиваемые пути приложения.
/// Этот класс сохраняется в settings.json и загружается при старте.
/// </summary>
public class AppSettings
{
    // --- Основные рабочие папки ---

    /// <summary>Базовая папка для создания проектов</summary>
    public string ProjectBaseFolder { get; set; } = @"D:\Projects\ПАНОРАМА";

    /// <summary>Шаблон .prproj файла для новых проектов</summary>
    public string SourceTemplateFile { get; set; } = @"\\Archive1\графичекое_оформление\Test.prproj";

    /// <summary>Папка для поиска готовых .mp4 файлов</summary>
    public string SearchFolder { get; set; } = @"D:\Projects\ПАНОРАМА";

    /// <summary>Дополнительная папка для поиска (может быть пустой)</summary>
    public string AdditionalSearchFolder { get; set; } = string.Empty;

    // --- Пути для копирования ПАНОРАМА / ДАЙДЖЕСТ ---

    /// <summary>Архив Site2: \\ARCHIVE2\Site2\{year}\{MM}_{MonthTitle}\{DD}</summary>
    public string Site2Archive { get; set; } = @"\\ARCHIVE2\Site2";

    /// <summary>Эфир (вещание): \\Archive1\export\ПАНОРАМА</summary>
    public string EfirPanorama { get; set; } = @"\\Archive1\export\ПАНОРАМА";

    /// <summary>Кодер Site: \\NEWS-ENCODER\Coder_SITE</summary>
    public string CoderSite { get; set; } = @"\\NEWS-ENCODER\Coder_SITE";

    // --- Пути для копирования НОВОСТИ ---

    /// <summary>Хранилище новостей: \\archive2\Архив_25К\НОВОСТИ\{year}\{MM}_{month_lower}</summary>
    public string NewsStorage { get; set; } = @"\\archive2\Архив_25К\НОВОСТИ";

    /// <summary>Эфир 25к новости: \\Archive2\25k\25k\НОВОСТИ</summary>
    public string NewsEfir25 { get; set; } = @"\\Archive2\25k\25k\НОВОСТИ";

    /// <summary>Кодер 25к: \\News-encoder25k\25K</summary>
    public string Coder25 { get; set; } = @"\\News-encoder25k\25K";

    // --- Пути для копирования АРХИВ ---

    /// <summary>Сюжеты панорамы: \\Archive2\сюжеты панорамы 2\{year}\{MM}_{MONTH_UPPER}</summary>
    public string ArchiveStories { get; set; } = @"\\Archive2\сюжеты панорамы 2";
}
