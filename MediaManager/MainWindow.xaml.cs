using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaManager.Models;
using MediaManager.ViewModels;

namespace MediaManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;

    public MainWindow()
    {
        InitializeComponent();

        var settingsViewModel = new SettingsViewModel();
        _mainViewModel = new MainViewModel(settingsViewModel);

        DataContext = _mainViewModel;
        settingsPanel.DataContext = settingsViewModel;

        // Подписываемся на нажатия клавиш для всего окна
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    // --- Горячие клавиши ---

    /// <summary>
    /// Обработчик горячих клавиш для всего окна.
    /// PreviewKeyDown срабатывает ДО того, как элемент управления обработает клавишу,
    /// поэтому мы можем перехватить F5, Escape и т.д.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // F5 — обновить список файлов
            case Key.F5:
                _mainViewModel.RefreshCommand.Execute(null);
                e.Handled = true; // Говорим WPF: «мы обработали, дальше не передавай»
                break;

            // Escape — закрыть настройки (если открыты)
            case Key.Escape:
                if (_mainViewModel.IsSettingsVisible)
                {
                    _mainViewModel.IsSettingsVisible = false;
                    e.Handled = true;
                }
                break;

            // Enter — создать проект (только если курсор в поле ввода имени проекта)
            case Key.Enter:
                if (Keyboard.FocusedElement is TextBox textBox &&
                    textBox.GetBindingExpression(TextBox.TextProperty)?.ResolvedSourcePropertyName == "ProjectName")
                {
                    _mainViewModel.CreateProjectCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    // --- Кнопки копирования ---

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

        await _mainViewModel.ExecuteCopyAsync(file, destinationKey);
    }

    // --- Title bar ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            if (WindowState == WindowState.Maximized)
            {
                var point = e.GetPosition(this);
                double proportionX = point.X / ActualWidth;
                WindowState = WindowState.Normal;
                Left = Mouse.GetPosition(null).X - (Width * proportionX);
                Top = 0;
            }
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            maximizeButton.Content = "☐";
        }
        else
        {
            WindowState = WindowState.Maximized;
            maximizeButton.Content = "❐";
        }
    }
}
