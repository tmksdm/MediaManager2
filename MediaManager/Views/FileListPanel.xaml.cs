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
/// 
/// Клавиатурная навигация:
///   ↑↓ — перемещение по файлам (стандартное поведение ListBox)
///   Enter — открыть выделенный файл в плеере
///   Правый клик мыши — открыть папку в Проводнике (не меняется)
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
    // === Клавиатура: Enter на выделенном файле ===
    // ======================================================

    /// <summary>
    /// Обработчик клавиатуры для ListBox файлов.
    /// Enter — открыть выделенный файл.
    /// Стрелки ↑↓ обрабатываются ListBox автоматически,
    /// а на границе карточки выделение переходит в соседнюю карточку.
    /// </summary>
    private void FileListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        if (e.Key == Key.Enter && listBox.SelectedItem is MediaFile file)
        {
            OpenFileInShell(file.FullPath);
            e.Handled = true;
            return;
        }

        // Переход между карточками папок: ↓ на последней строке
        // переносит выделение в следующую карточку, ↑ на первой — в предыдущую
        if (e.Key is Key.Down or Key.Up)
        {
            bool atBoundary =
                (e.Key == Key.Down && listBox.SelectedIndex == listBox.Items.Count - 1) ||
                (e.Key == Key.Up && listBox.SelectedIndex <= 0);

            if (!atBoundary)
                return;

            var target = FindNeighborListBox(listBox, next: e.Key == Key.Down);
            if (target == null || target.Items.Count == 0)
                return;

            int index = e.Key == Key.Down ? 0 : target.Items.Count - 1;
            if (target.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container)
                return;

            // Снимаем выделение в прежней карточке, чтобы выделенным был только один сюжет
            listBox.SelectedIndex = -1;

            target.SelectedItem = target.Items[index];
            container.BringIntoView();
            container.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ищет соседний ListBox файлов: следующий или предыдущий
    /// по вертикальному расположению карточек на экране.
    /// Список зациклен: после последней карточки идёт первая,
    /// перед первой — последняя.
    /// </summary>
    private ListBox? FindNeighborListBox(ListBox current, bool next)
    {
        var all = new List<(ListBox Box, double Y)>();
        CollectListBoxes(this, all);
        if (all.Count == 0)
            return null;

        all.Sort((a, b) => a.Y.CompareTo(b.Y));
        int i = all.FindIndex(x => ReferenceEquals(x.Box, current));
        if (i < 0)
            return null;

        int targetIndex = next ? i + 1 : i - 1;
        if (targetIndex >= all.Count)
            targetIndex = 0;
        else if (targetIndex < 0)
            targetIndex = all.Count - 1;
        return all[targetIndex].Box;
    }

    /// <summary>
    /// Собирает все ListBox файлов внутри панели с их вертикальными координатами.
    /// </summary>
    private void CollectListBoxes(DependencyObject parent, List<(ListBox Box, double Y)> result)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ListBox listBox)
            {
                double y = listBox.TransformToAncestor(this).Transform(new Point(0, 0)).Y;
                result.Add((listBox, y));
            }
            CollectListBoxes(child, result);
        }
    }

    // ======================================================
    // === Колёсико мыши: прокрутка списка сюжетов ===
    // ======================================================

    /// <summary>
    /// Прокрутка списка колёсиком мыши.
    /// Внутренние ListBox карточек перехватывают событие колёсика,
    /// но сами прокручивать нечего — из-за этого список не двигался
    /// над строками файлов. PreviewMouseWheel перехватывает событие
    /// раньше (туннелирование) и прокручивает внешний ScrollViewer.
    /// </summary>
    private void FileListScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        FileListScrollViewer.ScrollToVerticalOffset(
            FileListScrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
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
    /// Дополнительно выделяем строку в ListBox и передаём ей клавиатурный фокус,
    /// чтобы стрелки ↑↓ шли именно от этой строки.
    /// </summary>
    private void FileRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not MediaFile file)
            return;

        // Выделяем строку в ListBox и передаём фокус на ListBoxItem
        var listBoxItem = FindAncestor<ListBoxItem>(element);
        var listBox = FindAncestor<ListBox>(element);
        if (listBox != null && listBoxItem != null)
        {
            listBox.SelectedItem = file;
            // Фокус на конкретный ListBoxItem — стрелки пойдут от него
            listBoxItem.Focus();
        }

        OpenFileInShell(file.FullPath);
        e.Handled = true;
    }

    /// <summary>
    /// Ищет родительский элемент указанного типа в визуальном дереве.
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T result)
                return result;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
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
