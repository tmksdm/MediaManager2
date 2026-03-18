using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

        // Подключаем обработку ресайза через WinAPI после загрузки окна
        SourceInitialized += MainWindow_SourceInitialized;
    }

    // ======================================================
    // === Ресайз окна через WinAPI (замена системной рамки) ===
    // ======================================================

    // Толщина невидимой зоны захвата по краям окна (в пикселях)
    private const int ResizeBorderWidth = 6;

    // Коды зон окна для Windows
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    // Сообщение Windows: «определи, в какой зоне окна находится курсор»
    private const int WM_NCHITTEST = 0x0084;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Подключаемся к системным сообщениям окна, чтобы перехватить WM_NCHITTEST.
    /// Это позволяет Windows знать, что края окна — это зоны для ресайза.
    /// </summary>
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    /// <summary>
    /// Обработчик системных сообщений окна.
    /// Когда Windows спрашивает «где мышь?» (WM_NCHITTEST),
    /// мы проверяем — если мышь у края окна, говорим «это зона ресайза».
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
        {
            // Получаем координаты мыши относительно окна
            var point = PointFromScreen(new Point(
                (short)(lParam.ToInt32() & 0xFFFF),
                (short)(lParam.ToInt32() >> 16)));

            double w = ActualWidth;
            double h = ActualHeight;
            int border = ResizeBorderWidth;

            // Определяем зону: углы и стороны
            bool left = point.X < border;
            bool right = point.X > w - border;
            bool top = point.Y < border;
            bool bottom = point.Y > h - border;

            if (top && left) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (top && right) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (bottom && left) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (bottom && right) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (left) { handled = true; return new IntPtr(HTLEFT); }
            if (right) { handled = true; return new IntPtr(HTRIGHT); }
            if (top) { handled = true; return new IntPtr(HTTOP); }
            if (bottom) { handled = true; return new IntPtr(HTBOTTOM); }
        }

        return IntPtr.Zero;
    }

    // ======================================================
    // === Горячие клавиши ===
    // ======================================================

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

        await _mainViewModel.ExecuteCopyAsync(file, destinationKey);
    }

    // ======================================================
    // === Title bar ===
    // ======================================================

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
