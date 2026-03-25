using MediaManager.Models;
using MediaManager.Services;
using MediaManager.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading; // для DispatcherTimer

namespace MediaManager.ViewModels;

/// <summary>
/// Главная ViewModel приложения.
/// Разбита на несколько файлов через partial class:
///   MainViewModel.cs                — поля, свойства, конструктор, команды
///   MainViewModel.FileWatchers.cs   — FileSystemWatcher (файлы + проекты)
///   MainViewModel.Navigation.cs     — навигация по датам (◀ ▶)
///   MainViewModel.Scanning.cs       — сканирование файлов и проверка статусов
///   MainViewModel.CopyOperations.cs — очередь копирования, обработка, журнал
///   MainViewModel.Projects.cs       — создание проектов и экспортные имена
/// </summary>
public partial class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly FileDiscoveryService _discoveryService = new();
    private readonly FileCopyService _copyService = new();
    private readonly ProjectCreationService _projectService = new();
    private readonly SettingsViewModel _settingsViewModel;

    /// <summary>Максимум дней для поиска ближайшей даты с файлами</summary>
    private const int MaxSearchDays = 365;

    /// <summary>Максимум записей в журнале копирований (чтобы не копить бесконечно)</summary>
    private const int MaxLogEntries = 50;

    /// <summary>
    /// Токен отмены текущего копирования.
    /// Создаётся перед каждым копированием, отменяется по кнопке «Отмена».
    /// </summary>
    private CancellationTokenSource? _copyCts;

    // ======================================================
    // === FileSystemWatcher — поля (реализация в MainViewModel.FileWatchers.cs) ===
    // ======================================================

    /// <summary>Watcher для основной папки поиска</summary>
    private FileSystemWatcher? _watcher1;

    /// <summary>Watcher для дополнительной папки поиска</summary>
    private FileSystemWatcher? _watcher2;

    /// <summary>
    /// Таймер для debounce: когда файл появляется / удаляется,
    /// мы не сканируем сразу, а ждём 500мс — вдруг ещё события придут.
    /// Это нужно потому что FileSystemWatcher часто шлёт несколько
    /// событий подряд на один и тот же файл (Created + Changed и т.д.).
    /// </summary>
    private CancellationTokenSource? _debounceCts;

    /// <summary>Задержка перед автообновлением (мс)</summary>
    private const int DebounceDelayMs = 500;

    /// <summary>
    /// Watcher для папки проектов (ProjectBaseFolder).
    /// Следит за созданием / удалением / переименованием ПАПОК.
    /// При срабатывании — обновляет выпадающий список проектов.
    /// </summary>
    private FileSystemWatcher? _projectWatcher;

    /// <summary>Отдельный debounce для обновления списка проектов</summary>
    private CancellationTokenSource? _projectDebounceCts;

    // ======================================================
    // === Таймер периодической проверки статусов ===
    // ======================================================

    /// <summary>
    /// Таймер, который раз в 30 секунд тихо пересканирует статусы
    /// копирования файлов, отображаемых на экране.
    /// Это нужно, чтобы кнопки автоматически «сбрасывались»,
    /// если кто-то удалил файл из конечной папки.
    /// Не создаёт нагрузки — проверяются только файлы текущей даты.
    /// </summary>
    private readonly DispatcherTimer _statusRecheckTimer;

    /// <summary>Интервал фоновой проверки статусов (секунды)</summary>
    private const int StatusRecheckIntervalSeconds = 30;

    /// <summary>Флаг: идёт ли фоновая проверка статусов (защита от наложения)</summary>
    private bool _isRecheckingStatuses;

    // ======================================================
    // === Свойства ===
    // ======================================================

    private DateTime _selectedDate = DateTime.Today;
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDateText));

                // При смене даты обновляем список проектов.
                // Сбрасываем выбранный проект и панель экспортных имён,
                // потому что они относились к предыдущей дате.
                SelectedProject = null;
                RefreshProjectsForSelectedDate();

                // Запускаем асинхронное сканирование файлов
                ScanFilesAsync();
            }
        }
    }

    public string SelectedDateText => _selectedDate.ToString("dd.MM.yyyy");

    private string _projectName = string.Empty;
    public string ProjectName
    {
        get => _projectName;
        set { if (_projectName != value) { _projectName = value; OnPropertyChanged(); } }
    }

    private bool _isSettingsVisible = false;
    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set { if (_isSettingsVisible != value) { _isSettingsVisible = value; OnPropertyChanged(); } }
    }

    private string _statusMessage = "Готово";
    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    private ObservableCollection<FolderGroup> _folderGroups = new();
    public ObservableCollection<FolderGroup> FolderGroups
    {
        get => _folderGroups;
        set { _folderGroups = value; OnPropertyChanged(); }
    }

    private int _totalFilesFound;
    public int TotalFilesFound
    {
        get => _totalFilesFound;
        set { if (_totalFilesFound != value) { _totalFilesFound = value; OnPropertyChanged(); } }
    }

    public bool IsEmpty => FolderGroups.Count == 0;

    /// <summary>Блокировка кнопок во время копирования</summary>
    private bool _isCopying = false;
    public bool IsCopying
    {
        get => _isCopying;
        set
        {
            if (_isCopying != value)
            {
                _isCopying = value;
                OnPropertyChanged();
                // Принудительно обновляем CanExecute всех команд —
                // без этого кнопка «Отмена» остаётся неактивной,
                // потому что WPF не знает, что нужно перепроверить CancelCopyCommand.
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>Прогресс копирования (0–100)</summary>
    private double _copyProgress;
    public double CopyProgress
    {
        get => _copyProgress;
        set { if (Math.Abs(_copyProgress - value) > 0.1) { _copyProgress = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Идёт ли поиск ближайшей даты (для блокировки кнопок ◀ ▶).
    /// Пока true — кнопки навигации недоступны, в статусе «Поиск...»
    /// </summary>
    private bool _isNavigating = false;
    public bool IsNavigating
    {
        get => _isNavigating;
        set { if (_isNavigating != value) { _isNavigating = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Идёт ли сканирование файлов.
    /// Пока true — кнопка «Обновить» неактивна, в статусе «Сканирование...»
    /// </summary>
    private bool _isScanning = false;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                OnPropertyChanged();
                // Принудительно обновляем состояние всех команд (CanExecute).
                // Без этого кнопка «Обновить» остаётся бледной после сканирования,
                // потому что WPF не сразу перепроверяет CanExecute.
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    // ======================================================
    // === Журнал копирований ===
    // ======================================================

    /// <summary>
    /// Журнал копирований за текущую сессию.
    /// Новые записи добавляются в начало (самое свежее — сверху).
    /// Максимум MaxLogEntries записей.
    /// </summary>
    private ObservableCollection<CopyLogEntry> _copyLog = new();
    public ObservableCollection<CopyLogEntry> CopyLog
    {
        get => _copyLog;
        set { _copyLog = value; OnPropertyChanged(); }
    }

    /// <summary>Есть ли записи в журнале (для показа/скрытия панели)</summary>
    public bool HasLogEntries => CopyLog.Count > 0;

    /// <summary>Развёрнут ли журнал копирований</summary>
    private bool _isLogExpanded = true;
    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set { if (_isLogExpanded != value) { _isLogExpanded = value; OnPropertyChanged(); } }
    }

    // ======================================================
    // === Очередь копирования (видимая) ===
    // ======================================================

    /// <summary>
    /// Очередь задач копирования, привязанная к UI.
    /// ObservableCollection вместо Queue, потому что:
    /// 1) нужно показывать список в XAML (привязка)
    /// 2) нужно удалять элемент из середины (крестик на каждой задаче)
    /// Первый элемент (индекс 0) — текущий копируемый, остальные — ожидающие.
    /// </summary>
    private ObservableCollection<CopyQueueItem> _copyQueue = new();
    public ObservableCollection<CopyQueueItem> CopyQueue
    {
        get => _copyQueue;
        set { _copyQueue = value; OnPropertyChanged(); }
    }

    /// <summary>Есть ли задачи в очереди (для показа/скрытия панели)</summary>
    public bool HasQueueItems => CopyQueue.Count > 0;

    // ======================================================
    // === Свойства для выпадающего списка проектов ===
    // ======================================================

    /// <summary>Список проектов (имён подпапок) за выбранную дату</summary>
    private ObservableCollection<string> _todayProjects = new();
    public ObservableCollection<string> TodayProjects
    {
        get => _todayProjects;
        set { _todayProjects = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTodayProjects)); }
    }

    /// <summary>Есть ли проекты за выбранную дату (для показа треугольника ▼)</summary>
    public bool HasTodayProjects => TodayProjects.Count > 0;

    /// <summary>Открыт ли выпадающий список проектов</summary>
    private bool _isProjectListOpen = false;
    public bool IsProjectListOpen
    {
        get => _isProjectListOpen;
        set { if (_isProjectListOpen != value) { _isProjectListOpen = value; OnPropertyChanged(); } }
    }

    /// <summary>Выбранный проект из списка (имя подпапки)</summary>
    private string? _selectedProject;
    public string? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (_selectedProject != value)
            {
                _selectedProject = value;
                OnPropertyChanged();

                // Когда выбрали проект — генерируем имена для экспорта
                UpdateExportNames();
            }
        }
    }

    /// <summary>Список имён файлов для экспорта (для панели под строкой создания)</summary>
    private ObservableCollection<ExportName> _exportNames = new();
    public ObservableCollection<ExportName> ExportNames
    {
        get => _exportNames;
        set { _exportNames = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExportNames)); }
    }

    /// <summary>Есть ли имена для показа</summary>
    public bool HasExportNames => ExportNames.Count > 0;

    // ======================================================
    // === Команды ===
    // ======================================================

    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public RelayCommand GoToTodayCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CreateProjectCommand { get; }
    public RelayCommand ToggleProjectListCommand { get; }
    public RelayCommand SelectProjectCommand { get; }
    public RelayCommand CopyExportNameCommand { get; }
    public RelayCommand CancelCopyCommand { get; }
    public RelayCommand ToggleLogCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    /// <summary>Удалить конкретную задачу из очереди (крестик в панели очереди)</summary>
    public RelayCommand RemoveFromQueueCommand { get; }

    /// <summary>Очистить всю очередь (оставив текущее копирование)</summary>
    public RelayCommand ClearQueueCommand { get; }

    // ======================================================
    // === Конструктор ===
    // ======================================================

    public MainViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;

        // Умная навигация: ищем ближайшую дату с файлами (async, не блокирует UI)
        NavigateBackCommand = new RelayCommand(
            _ => NavigateToNearestDateAsync(-1),
            _ => !IsNavigating);
        NavigateForwardCommand = new RelayCommand(
            _ => NavigateToNearestDateAsync(+1),
            _ => !IsNavigating);

        GoToTodayCommand = new RelayCommand(_ => SelectedDate = DateTime.Today);
        RefreshCommand = new RelayCommand(
            _ => ScanFilesAsync(),
            _ => !IsScanning);
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsVisible = !IsSettingsVisible);
        CreateProjectCommand = new RelayCommand(_ => ExecuteCreateProject());

        // Команды для списка проектов
        ToggleProjectListCommand = new RelayCommand(_ => ToggleProjectList());
        SelectProjectCommand = new RelayCommand(param => SelectProject(param as string));
        CopyExportNameCommand = new RelayCommand(param => CopyExportName(param as string));

        // Команда отмены копирования — отменяет текущее + очищает очередь
        CancelCopyCommand = new RelayCommand(_ => CancelCopy(), _ => IsCopying);

        // Команды очереди
        RemoveFromQueueCommand = new RelayCommand(param => RemoveFromQueue(param));
        ClearQueueCommand = new RelayCommand(_ => ClearQueue());

        // Команды журнала
        ToggleLogCommand = new RelayCommand(_ => IsLogExpanded = !IsLogExpanded);
        ClearLogCommand = new RelayCommand(_ => { CopyLog.Clear(); OnPropertyChanged(nameof(HasLogEntries)); });

        // Подписываемся на изменение настроек — пересоздадим FileSystemWatcher
        _settingsViewModel.SettingsChanged += OnSettingsChanged;

        // Создаём таймер периодической проверки статусов копирования.
        // DispatcherTimer работает в UI-потоке — его Tick безопасно обращается к свойствам.
        // Тяжёлая работа (File.Exists по сети) уходит в Task.Run внутри обработчика.
        _statusRecheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(StatusRecheckIntervalSeconds)
        };
        _statusRecheckTimer.Tick += async (_, _) => await RecheckCopyStatusesAsync();
        _statusRecheckTimer.Start();

        // Первое сканирование при запуске
        ScanFilesAsync();

        // Загружаем список проектов за выбранную дату (сегодня) при старте
        RefreshProjectsForSelectedDate();

        // Запускаем FileSystemWatcher на текущие папки поиска
        SetupFileWatchers();

        // Запускаем FileSystemWatcher на папку проектов
        SetupProjectWatcher();
    }

    // ======================================================
    // === Освобождение ресурсов ===
    // ======================================================

    /// <summary>
    /// Освобождение ресурсов. Вызывается из MainWindow при закрытии.
    /// </summary>
    public void Cleanup()
    {
        // Останавливаем таймер проверки статусов
        _statusRecheckTimer.Stop();

        DisposeFileWatchers();
        DisposeProjectWatcher();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _projectDebounceCts?.Cancel();
        _projectDebounceCts?.Dispose();
        _settingsViewModel.SettingsChanged -= OnSettingsChanged;
    }
}
