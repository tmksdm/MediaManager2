using System.Windows;
using MediaManager.Services;

namespace MediaManager;

/// <summary>
/// Точка входа приложения.
/// Здесь перехватываем все необработанные исключения,
/// чтобы программа не падала молча, а записывала ошибку в лог.
/// Также применяем тему (светлая/тёмная) до показа главного окна.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Применяем сохранённую тему ДО показа окна.
        // Настройки загружаются из settings.json.
        // ThemeService заменяет словарь [1] в MergedDictionaries.
        var settings = SettingsService.Load();
        ThemeService.ApplyTheme(settings.IsDarkTheme);

        // Перехват необработанных исключений в UI-потоке
        DispatcherUnhandledException += (sender, args) =>
        {
            LogService.Error("Необработанное исключение (UI)", args.Exception);

            // Предлагаем пользователю выбор: закрыть или продолжить.
            // В ньюсруме важнее не потерять работу, но после ошибки
            // внутреннее состояние может быть нарушено — рекомендуем перезапуск.
            var result = MessageBox.Show(
                $"Произошла ошибка:\n\n{args.Exception.Message}\n\n" +
                "Подробности записаны в log.txt.\n\n" +
                "Рекомендуется перезапустить программу.\n" +
                "Закрыть программу?",
                "Ошибка",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                args.Handled = false;
            }
            else
            {
                args.Handled = true;
            }
        };

        // Перехват необработанных исключений в фоновых потоках
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                LogService.Error("Необработанное исключение (фон)", ex);
            }
        };

        // Перехват ошибок в async-задачах (Task), которые никто не await-ил
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogService.Error("Необработанное исключение (Task)", args.Exception);
            args.SetObserved();
        };
    }
}
