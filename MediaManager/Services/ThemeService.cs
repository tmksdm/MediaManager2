using System.Windows;

namespace MediaManager.Services;

/// <summary>
/// Управляет переключением темы (светлая/тёмная).
/// 
/// Принцип работы:
/// В App.xaml подключены два ResourceDictionary:
///   [0] — ButtonStyles.xaml (стили кнопок, не меняется)
///   [1] — LightTheme.xaml или DarkTheme.xaml (цвета)
/// 
/// При переключении темы мы заменяем словарь [1] на другой.
/// Все элементы, использующие {DynamicResource ...}, обновятся автоматически.
/// </summary>
public static class ThemeService
{
    /// <summary>Индекс словаря темы в MergedDictionaries</summary>
    private const int ThemeDictionaryIndex = 1;

    private static readonly Uri LightThemeUri = new("Styles/LightTheme.xaml", UriKind.Relative);
    private static readonly Uri DarkThemeUri = new("Styles/DarkTheme.xaml", UriKind.Relative);

    /// <summary>
    /// Применить тему при старте приложения.
    /// Вызывается из App.xaml.cs до показа окна.
    /// </summary>
    public static void ApplyTheme(bool isDark)
    {
        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        // Загружаем нужный словарь
        var themeUri = isDark ? DarkThemeUri : LightThemeUri;
        var newTheme = new ResourceDictionary { Source = themeUri };

        // Заменяем словарь темы (индекс 1)
        if (mergedDictionaries.Count > ThemeDictionaryIndex)
        {
            mergedDictionaries[ThemeDictionaryIndex] = newTheme;
        }
        else
        {
            // Первый запуск — добавляем
            mergedDictionaries.Add(newTheme);
        }
    }

    /// <summary>
    /// Переключить тему на противоположную.
    /// Возвращает true если теперь тёмная, false если светлая.
    /// </summary>
    public static bool ToggleTheme(bool currentlyDark)
    {
        bool newIsDark = !currentlyDark;
        ApplyTheme(newIsDark);
        return newIsDark;
    }
}
