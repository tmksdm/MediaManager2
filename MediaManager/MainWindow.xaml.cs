using System.Windows;
using System.Windows.Input;
using MediaManager.ViewModels;

namespace MediaManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Создаём SettingsViewModel (загружает настройки из файла)
        var settingsViewModel = new SettingsViewModel();

        // Создаём MainViewModel и передаём ему настройки
        var mainViewModel = new MainViewModel(settingsViewModel);

        // Устанавливаем контексты данных
        DataContext = mainViewModel;
        settingsPanel.DataContext = settingsViewModel;
    }

    // ==============================
    // Кастомный заголовок окна
    // ==============================

    /// <summary>
    /// Перетаскивание окна за синюю шапку.
    /// Двойной клик — развернуть/восстановить.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Двойной клик — переключаем развёрнутое/обычное состояние
            ToggleMaximize();
        }
        else
        {
            // Одинарный клик — перетаскивание окна
            // Если окно развёрнуто, сначала восстанавливаем его
            if (WindowState == WindowState.Maximized)
            {
                // Запоминаем позицию мыши относительно окна
                var point = e.GetPosition(this);
                double proportionX = point.X / ActualWidth;

                WindowState = WindowState.Normal;

                // Перемещаем окно так, чтобы мышь осталась пропорционально на том же месте
                Left = Mouse.GetPosition(null).X - (Width * proportionX);
                Top = 0;
            }
            DragMove();
        }
    }

    /// <summary>Кнопка «Свернуть»</summary>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>Кнопка «Развернуть / Восстановить»</summary>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    /// <summary>Кнопка «Закрыть»</summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Переключить между развёрнутым и обычным состоянием.
    /// Также обновляет иконку кнопки.
    /// </summary>
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
