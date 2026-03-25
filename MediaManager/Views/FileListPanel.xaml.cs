using MediaManager.Models;
using MediaManager.Services;
using MediaManager.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MediaManager.Views;

/// <summary>
/// Панель списка файлов.
/// Содержит Click-хэндлеры для кнопок копирования и открытия файлов/папок.
/// DataContext наследуется от родительского окна (MainViewModel).
/// MainViewModel берётся через Window.DataContext.
/// </summary>
public partial class FileListPanel : UserControl
{
    public FileListPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Получает MainViewModel из DataContext родительского окна.
    /// UserControl наследует DataContext от окна, но для надёжности
    /// ищем окно явно через Window.GetWindow().
    /// </summary>
    private MainViewModel? GetMainViewModel()
    {
        var window = Window.GetWindow(this);
        return window?.DataContext as MainViewModel;
    }

    // ======================================================
    // === Кнопки копирования ===
    // ======================================================

    /// <summary>
    /// Обработчик нажатия любой кнопки копирования.
    /// Кнопка хранит ключ направления в свойстве Tag,
    /// а DataContext кнопки — это MediaFile.
    /// </summary>
    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is not MediaFile file)
            return;

        if (button.Tag is not string destinationKey)
            return;

        var vm = GetMainViewModel();
        if (vm == null)
            return;

        await vm.ExecuteCopyAsync(file, destinationKey);
    }

    // ======================================================
    // === Открытие файла / папки ===
    // ======================================================

    /// <summary>
    /// Левый клик по имени файла — открыть файл в плеере по умолчанию.
    /// </summary>
    private void FileRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not MediaFile file)
            return;

        OpenFileInShell(file.FullPath);
        e.Handled = true;
    }

    /// <summary>
    /// Правый клик по имени файла — открыть папку в Проводнике с выделенным файлом.
    /// e.Handled = true блокирует стандартное контекстное меню.
    /// </summary>
    private void FileRow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not MediaFile file)
            return;

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
        }
        catch (Exception ex)
        {
            var vm = GetMainViewModel();
            if (vm != null)
                vm.StatusMessage = $"❌ Не удалось открыть папку: {ex.Message}";
            LogService.Error("Ошибка открытия папки", ex);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Открывает файл через системную ассоциацию (UseShellExecute).
    /// .mp4 обычно открывается в видеоплеере (VLC, встроенный плеер и т.д.).
    /// </summary>
    private void OpenFileInShell(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var vm = GetMainViewModel();
            if (vm != null)
                vm.StatusMessage = $"❌ Не удалось открыть файл: {ex.Message}";
            LogService.Error("Ошибка открытия файла", ex);
        }
    }
}
