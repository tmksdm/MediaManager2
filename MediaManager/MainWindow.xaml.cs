using MediaManager.Models;
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

    /// <summary>
    /// Запоминаем размер и позицию окна в Normal-состоянии,
    /// чтобы при закрытии из Maximized сохранить именно Normal-размеры.
    /// Иначе при следующем запуске окно будет на весь экран без возможности вернуться.
    /// </summary>
    private double _restoreLeft;
    private double _restoreTop;
    private double _restoreWidth;
    private double _restoreHeight;

    public MainWindow()
    {
        InitializeComponent();

        var settingsViewModel = new SettingsViewModel();
        _mainViewModel = new MainViewModel(settingsViewModel);

        DataContext = _mainViewModel;
        settingsPanel.DataContext = settingsViewModel;

        // Загружаем позицию и размер окна из настроек
        RestoreWindowPosition(settingsViewModel.GetSettings());

        // Подписываемся на нажатия клавиш для всего окна
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Подключаем обработку ресайза через WinAPI после загрузки окна
        SourceInitialized += MainWindow_SourceInitialized;

        // Закрытие Popup при клике за его пределами (для программного открытия)
        PreviewMouseLeftButtonDown += MainWindow_PreviewMouseLeftButtonDown;

        // При закрытии — сохраняем позицию и освобождаем ресурсы
        Closed += MainWindow_Closed;

        // Запоминаем Normal-размеры при каждом перемещении/ресайзе
        LocationChanged += (_, _) => RememberNormalBounds();
        SizeChanged += (_, _) => RememberNormalBounds();
    }

    // ======================================================
    // === Запоминание и восстановление позиции окна ===
    // ======================================================

    /// <summary>
    /// Восстанавливает позицию и размер окна из настроек.
    /// Если это первый запуск (Left/Top = null) — центрируем по экрану.
    /// Если сохранённая позиция за пределами экранов — тоже центрируем.
    /// </summary>
    private void RestoreWindowPosition(AppSettings settings)
    {
        // Устанавливаем размер
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            double left = settings.WindowLeft.Value;
            double top = settings.WindowTop.Value;

            // Проверяем, что окно хотя бы частично видно на каком-нибудь мониторе
            if (IsPositionOnScreen(left, top, Width, Height))
            {
                Left = left;
                Top = top;
            }
            else
            {
                // Позиция вне экрана — центрируем
                CenterOnScreen();
            }
        }
        else
        {
            // Первый запуск — центрируем
            CenterOnScreen();
        }

        // Запоминаем Normal-размеры до возможного Maximize
        _restoreLeft = Left;
        _restoreTop = Top;
        _restoreWidth = Width;
        _restoreHeight = Height;

        // Если было развёрнуто — разворачиваем
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

    /// <summary>
    /// Ищет монитор, на котором находится прямоугольник.
    /// MONITOR_DEFAULTTONULL (0) — вернёт IntPtr.Zero, если прямоугольник
    /// не пересекается ни с одним монитором.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    private const uint MONITOR_DEFAULTTONULL = 0;

    /// <summary>
    /// Проверяет, что окно хотя бы частично видно на каком-нибудь мониторе.
    /// Используем WinAPI MonitorFromRect — он возвращает null, если
    /// прямоугольник не пересекается ни с одним монитором.
    /// </summary>
    private static bool IsPositionOnScreen(double left, double top, double width, double height)
    {
        // Уменьшаем прямоугольник проверки — требуем, чтобы хотя бы
        // 100×100 пикселей окна были на экране (а не 1 пиксель уголка)
        const int minVisible = 100;

        var rect = new RECT
        {
            Left = (int)left + minVisible,
            Top = (int)top + minVisible,
            Right = (int)(left + width) - minVisible,
            Bottom = (int)(top + height) - minVisible
        };

        // Если после сужения прямоугольник стал невалидным — считаем «вне экрана»
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return false;

        IntPtr monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL);
        return monitor != IntPtr.Zero;
    }

    /// <summary>
    /// Центрирует окно на основном мониторе.
    /// </summary>
    private void CenterOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = (workArea.Width - Width) / 2 + workArea.Left;
        Top = (workArea.Height - Height) / 2 + workArea.Top;
    }

    /// <summary>
    /// Запоминает текущие Normal-размеры окна.
    /// Вызывается при каждом перемещении/ресайзе.
    /// В Maximized-состоянии — не обновляем (чтобы не потерять Normal-координаты).
    /// </summary>
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

    /// <summary>
    /// Сохраняет позицию и размер окна в настройки перед закрытием.
    /// Всегда сохраняем Normal-координаты (даже если закрываем из Maximized).
    /// </summary>
    private void SaveWindowPosition()
    {
        var settings = ((SettingsViewModel)settingsPanel.DataContext).GetSettings();

        // Сохраняем Normal-координаты (запомненные до Maximize)
        settings.WindowLeft = _restoreLeft;
        settings.WindowTop = _restoreTop;
        settings.WindowWidth = _restoreWidth;
        settings.WindowHeight = _restoreHeight;
        settings.WindowMaximized = WindowState == WindowState.Maximized;

        SettingsService.Save(settings);
    }

    /// <summary>
    /// При закрытии окна: сохраняем позицию + освобождаем ресурсы ViewModel
    /// (FileSystemWatcher, таймеры debounce, подписки на события).
    /// </summary>
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveWindowPosition();
        _mainViewModel.Cleanup();
    }

    // ======================================================
    // === Ресайз окна через WinAPI (замена системной рамки) ===
    // ======================================================

    // Толщина невидимой зоны захвата по краям окна (в пикселях)
    private const int ResizeBorderWidth = 6;

    // Коды зон окна для Windows
    private const int HTCLIENT = 1;
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
    ///
    /// Координаты из lParam — в физических пикселях экрана.
    /// Сравниваем с размером окна тоже в физических пикселях (через DPI-коэффициент),
    /// чтобы зона ресайза была ровно ResizeBorderWidth пикселей независимо от масштаба.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && WindowState != WindowState.Maximized)
        {
            // Извлекаем экранные координаты мыши (физические пиксели)
            long lp = lParam.ToInt64();
            int screenX = (int)(short)(lp & 0xFFFF);
            int screenY = (int)(short)((lp >> 16) & 0xFFFF);

            // Получаем позицию и размер окна в физических пикселях через DPI
            double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            double winLeft = Left * dpiScale;
            double winTop = Top * dpiScale;
            double winWidth = ActualWidth * dpiScale;
            double winHeight = ActualHeight * dpiScale;

            // Координаты мыши относительно окна (в физических пикселях)
            double relX = screenX - winLeft;
            double relY = screenY - winTop;

            // Если координаты за пределами окна — не перехватываем
            if (relX < 0 || relY < 0 || relX > winWidth || relY > winHeight)
                return IntPtr.Zero;

            int border = ResizeBorderWidth; // уже в физических пикселях

            // Определяем зону: углы и стороны
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
    // === Кнопка отмены копирования ===
    // ======================================================

    /// <summary>
    /// Click handler для кнопки «Отмена» в статусной строке.
    /// Используем Click вместо Command — надёжнее работает
    /// в borderless-окне с AllowsTransparency.
    /// </summary>
    private void CancelCopyButton_Click(object sender, RoutedEventArgs e)
    {
        _mainViewModel.CancelCopy();
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
            _mainViewModel.StatusMessage = $"❌ Не удалось открыть папку: {ex.Message}";
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
                UseShellExecute = true // Открыть через ассоциацию Windows
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.StatusMessage = $"❌ Не удалось открыть файл: {ex.Message}";
            LogService.Error("Ошибка открытия файла", ex);
        }
    }
}
