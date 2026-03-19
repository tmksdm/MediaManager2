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

        // Закрытие Popup при клике за его пределами (для программного открытия)
        PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;
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

    /// <summary>
    /// Перетаскивание окна за title bar.
    /// Двойной клик — развернуть/свернуть.
    /// Drag из Maximized — окно переходит в Normal и следует за мышью.
    /// GetCursorPos (Win32) используется вместо Mouse.GetPosition(null),
    /// чтобы корректно работать на multi-monitor с разным DPI.
    /// Проверка e.ButtonState перед DragMove() предотвращает InvalidOperationException,
    /// если пользователь успел отпустить кнопку мыши.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Запоминаем пропорцию клика по ширине развёрнутого окна
            var point = e.GetPosition(this);
            double proportionX = point.X / ActualWidth;

            // Получаем абсолютные экранные координаты мыши через Win32
            // (надёжно работает на multi-monitor с разным DPI)
            GetCursorPos(out POINT cursorPos);

            // Переводим окно в Normal
            WindowState = WindowState.Normal;

            // Позиционируем окно так, чтобы курсор остался на прежнем месте title bar
            Left = cursorPos.X - (Width * proportionX);
            Top = cursorPos.Y - point.Y;
        }

        // Проверяем, что кнопка мыши всё ещё нажата —
        // иначе DragMove() бросит InvalidOperationException
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

    /// <summary>
    /// Клик по полю ввода имени проекта — открываем список проектов.
    /// </summary>
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

    /// <summary>
    /// Пользователь начал вводить текст — закрываем выпадающий список.
    /// </summary>
    private void ProjectNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_mainViewModel.IsProjectListOpen)
        {
            _mainViewModel.IsProjectListOpen = false;
        }
    }

    /// <summary>
    /// Клик в любом месте окна — если Popup открыт и клик был не по TextBox
    /// и не по самому Popup, закрываем его вручную.
    /// PreviewMouseLeftButtonDown на Window срабатывает ДО любого другого элемента.
    /// </summary>
    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_mainViewModel.IsProjectListOpen)
            return;

        // Проверяем: клик по TextBox ввода имени? — не закрываем
        if (projectNameTextBox.IsMouseOver)
            return;

        // Проверяем: клик по кнопке ▼? — не закрываем (она сама toggle-ит)
        if (projectDropdownButton.IsMouseOver)
            return;

        // Проверяем: клик внутри Popup? — не закрываем
        if (projectListPopup.Child is FrameworkElement popupContent && popupContent.IsMouseOver)
            return;

        // Клик за пределами — закрываем
        _mainViewModel.IsProjectListOpen = false;
    }


    // ======================================================
    // === Панель экспортных имён ===
    // ======================================================

    /// <summary>
    /// Закрыть панель имён для экспорта (крестик ✕).
    /// Сбрасываем выбранный проект — панель скроется.
    /// </summary>
    private void CloseExportPanel_Click(object sender, RoutedEventArgs e)
    {
        _mainViewModel.SelectedProject = null;
    }
}
