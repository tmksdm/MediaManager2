using System.Windows;
using MediaManager.Services;

namespace MediaManager;

/// <summary>
/// Точка входа приложения.
/// Здесь перехватываем все необработанные исключения,
/// чтобы программа не падала молча, а записывала ошибку в лог.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Перехват необработанных исключений в UI-потоке
        DispatcherUnhandledException += (sender, args) =>
        {
            LogService.Error("Необработанное исключение (UI)", args.Exception);

            MessageBox.Show(
                $"Произошла ошибка:\n\n{args.Exception.Message}\n\nПодробности записаны в log.txt",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Помечаем как обработанное — программа НЕ закроется, а продолжит работу
            args.Handled = true;
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
            args.SetObserved(); // Помечаем как обработанное
        };

        LogService.Info("Приложение запущено");
    }
}
