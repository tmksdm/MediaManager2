namespace MediaManager.Models;

/// <summary>
/// Строковые ключи направлений копирования.
/// Используются в FileCopyService (Label), SetCopiedFlag (switch),
/// ExecuteCopyAsync (проверка Эфир) и в XAML (Tag на кнопках).
/// 
/// Собраны в одном месте, чтобы опечатка не сломала связку
/// «кнопка → копирование → обновление статуса».
/// </summary>
public static class DestinationKeys
{
    public const string Site2 = "Site2 (архив)";
    public const string Efir = "Эфир";
    public const string CoderSite = "Coder Site";
    public const string Storage = "Архив 25к";
    public const string Efir25 = "Эфир 25к";
    public const string Coder25 = "Coder 25к";
    public const string Stories = "Сюжеты";
}
