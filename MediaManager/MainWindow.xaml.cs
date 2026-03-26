using MediaManager.Services;
using MediaManager.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MediaManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainViewModel;
    private readonly SettingsViewModel _settingsViewModel;

    /// <summary>
    /// Запоминаем размер и позицию окна в Normal-состоянии,
    /// чтобы при закрытии из Maximized сохранить именно Normal-размеры.
    /// </summary>
    private double _restoreLeft;
    private double _restoreTop;
    private double _restoreWidth;
    private double _restoreHeight;

    public MainWindow()
    {
        InitializeComponent();

        _settingsViewModel = new SettingsViewModel();
        _mainViewModel = new MainViewModel(_settingsViewModel);

        DataContext = _mainViewModel;
        settingsPanel.DataContext = _settingsViewModel;

        // Устанавливаем иконку кнопки темы при старте
        UpdateThemeButtonIcon();

        // Загружаем позицию и размер окна из настроек
        RestoreWindowPosition(_settingsViewModel.GetSettings());

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        SourceInitialized += MainWindow_SourceInitialized;
        PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;
        Closed += MainWindow_Closed;

        LocationChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();
    }

    // ======================================================
    // === Переключение темы ===
    // ======================================================

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.ToggleThemeCommand.Execute(null);
        UpdateThemeButtonIcon();
    }

    /// <summary>
    /// Обновляет иконку кнопки темы: 🌙 для светлой, ☀ для тёмной.
    /// </summary>
    private void UpdateThemeButtonIcon()
    {
        themeButtonIcon.Text = _settingsViewModel.IsDarkTheme ? "☀" : "🌙";
    }

    // ======================================================
    // === Запоминание и восстановление позиции окна ===
    // ======================================================

    private void RestoreWindowPosition(Models.AppSettings settings)
    {
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            double left = settings.WindowLeft.Value;
            double top = settings.WindowTop.Value;

            if (IsPositionOnScreen(left, top, Width, Height))
            {
                Left = left;
                Top = top;
            }
            else
            {
                CenterOnScreen();
            }
        }
        else
        {
            CenterOnScreen();
        }

        _restoreLeft = Left;
        _restoreTop = Top;
        _restoreWidth = Width;
        _restoreHeight = Height;

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            maximizeButton.Content = "❐";
        }
    }

    // --- WinAPI для проверки мониторов ---

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    private const uint MONITOR_DEFAULTTONULL = 0;

    private static bool IsPositionOnScreen(double left, double top, double width, double height)
    {
        const int minVisible = 100;

        var rect = new RECT
        {
            Left = (int)left + minVisible,
            Top = (int)top + minVisible,
            Right = (int)(left + width) - minVisible,
            Bottom = (int)(top + height) - minVisible
        };

        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return false;

        IntPtr monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL);
        return monitor != IntPtr.Zero;
    }

    private void CenterOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = (workArea.Width - Width) / 2 + workArea.Left;
        Top = (workArea.Height - Height) / 2 + workArea.Top;
    }

    private void RememberNormalBounds()
    {
        if (WindowState == WindowState.Normal)
        {
            _restoreLeft = Left;
            _restoreTop = Top;
            _restoreWidth = Width;
            _restoreHeight = Height;
        }
    }

    private void SaveWindowPosition()
    {
        var settings = _settingsViewModel.GetSettings();

        settings.WindowLeft = _restoreLeft;
        settings.WindowTop = _restoreTop;
        settings.WindowWidth = _restoreWidth;
        settings.WindowHeight = _restoreHeight;
        settings.WindowMaximized = WindowState == WindowState.Maximized;

        SettingsService.Save(settings);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveWindowPosition();
        _mainViewModel.Cleanup();
    }

    // ======================================================
    // === Ресайз окна через WinAPI ===
    // ======================================================

    private const int ResizeBorderWidth = 6;

    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private const int WM_NCHITTEST = 0x0084;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
        {
            long lp = lParam.ToInt64();
            int screenX = (int)(short)(lp & 0xFFFF);
            int screenY = (int)(short)((lp >> 16) & 0xFFFF);

            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double winLeft = Left * dpiScale;
            double winTop = Top * dpiScale;
            double winWidth = ActualWidth * dpiScale;
            double winHeight = ActualHeight * dpiScale;

            double relX = screenX - winLeft;
            double relY = screenY - winTop;

            if (relX < 0 || relY < 0 || relX > winWidth || relY > winHeight)
                return IntPtr.Zero;

            int border = ResizeBorderWidth;

            bool left = relX < border;
            bool right = relX > winWidth - border;
            bool top = relY < border;
            bool bottom = relY > winHeight - border;

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

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                _mainViewModel.RefreshCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (_mainViewModel.IsSettingsVisible)
                {
                    _mainViewModel.IsSettingsVisible = false;
                    e.Handled = true;
                }
                break;

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
    // === Кнопка отмены копирования ===
    // ======================================================

    private void CancelCopyButton_Click(object sender, RoutedEventArgs e)
    {
        _mainViewModel.CancelCopy();
    }

    // ======================================================
    // === Title bar ===
    // ======================================================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            var point = e.GetPosition(this);
            double proportionX = point.X / ActualWidth;

            GetCursorPos(out POINT cursorPos);

            WindowState = WindowState.Normal;

            Left = cursorPos.X - (Width * proportionX);
            Top = cursorPos.Y - point.Y;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
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

    // ======================================================
    // === Поле ввода проекта: открытие/закрытие списка ===
    // ======================================================

    private void ProjectNameTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mainViewModel.HasTodayProjects && !_mainViewModel.IsProjectListOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _mainViewModel.IsProjectListOpen = true;
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_mainViewModel.IsProjectListOpen)
        {
            _mainViewModel.IsProjectListOpen = false;
        }
    }

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_mainViewModel.IsProjectListOpen)
            return;

        if (projectNameTextBox.IsMouseOver)
            return;

        if (projectDropdownButton.IsMouseOver)
            return;

        if (projectListPopup.Child is FrameworkElement popupContent && popupContent.IsMouseOver)
            return;

        _mainViewModel.IsProjectListOpen = false;
    }

    // ======================================================
    // === Панель экспортных имён ===
    // ======================================================

    private void CloseExportPanel_Click(object sender, RoutedEventArgs e)
    {
        _mainViewModel.SelectedProject = null;
    }
}
