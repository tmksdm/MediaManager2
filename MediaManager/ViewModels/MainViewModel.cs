using MediaManager.Models;
using MediaManager.Services;
using MediaManager.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace MediaManager.ViewModels;

public class MainViewModel : INotifyPropertyChanged
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

    /// <summary>
    /// Токен отмены текущего копирования.
    /// Создаётся перед каждым копированием, отменяется по кнопке «Отмена».
    /// </summary>
    private CancellationTokenSource? _copyCts;

    // ======================================================
    // === FileSystemWatcher — автообновление списка файлов ===
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

    // --- Свойства ---

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
                // Запускаем асинхронное сканирование (async void — огонь и забудь)
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
    // === Свойства для выпадающего списка проектов ===
    // ======================================================

    /// <summary>Список проектов (имён подпапок) за сегодняшнюю дату</summary>
    private ObservableCollection<string> _todayProjects = new();
    public ObservableCollection<string> TodayProjects
    {
        get => _todayProjects;
        set { _todayProjects = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTodayProjects)); }
    }

    /// <summary>Есть ли проекты за сегодня (для показа треугольника ▼)</summary>
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

    // --- Команды ---

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

    public MainViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;

        // Умная навигация: ищем ближайшую дату с файлами (async, не блокирует UI)
        NavigateBackCommand = new RelayCommand(
            _ => NavigateToNearestDateAsync(-1),
            _ => !IsNavigating);  // Кнопка неактивна пока идёт поиск
        NavigateForwardCommand = new RelayCommand(
            _ => NavigateToNearestDateAsync(+1),
            _ => !IsNavigating);  // Кнопка неактивна пока идёт поиск

        GoToTodayCommand = new RelayCommand(_ => SelectedDate = DateTime.Today);
        RefreshCommand = new RelayCommand(
            _ => ScanFilesAsync(),
            _ => !IsScanning);  // Кнопка неактивна пока идёт сканирование
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsVisible = !IsSettingsVisible);
        CreateProjectCommand = new RelayCommand(_ => ExecuteCreateProject());

        // Новые команды для списка проектов
        ToggleProjectListCommand = new RelayCommand(_ => ToggleProjectList());
        SelectProjectCommand = new RelayCommand(param => SelectProject(param as string));
        CopyExportNameCommand = new RelayCommand(param => CopyExportName(param as string));

        // Команда отмены копирования — активна только пока идёт копирование
        CancelCopyCommand = new RelayCommand(_ => CancelCopy(), _ => IsCopying);

        // Подписываемся на изменение настроек — пересоздадим FileSystemWatcher
        _settingsViewModel.SettingsChanged += OnSettingsChanged;

        // Первое сканирование при запуске
        ScanFilesAsync();

        // Загружаем список проектов за сегодня при старте
        RefreshTodayProjects();

        // Запускаем FileSystemWatcher на текущие папки поиска
        SetupFileWatchers();
    }

    // ======================================================
    // === FileSystemWatcher — автообновление ===
    // ======================================================

    /// <summary>
    /// Создаёт FileSystemWatcher для папок поиска из настроек.
    /// Следит за появлением / удалением / переименованием .mp4 файлов.
    /// При любом изменении — автоматически обновляет список с debounce.
    /// </summary>
    private void SetupFileWatchers()
    {
        // Сначала убиваем старые watchers (если были)
        DisposeWatchers();

        var settings = _settingsViewModel.GetSettings();

        // Watcher для основной папки
        _watcher1 = CreateWatcher(settings.SearchFolder);

        // Watcher для дополнительной папки (если указана)
        _watcher2 = CreateWatcher(settings.AdditionalSearchFolder);
    }

    /// <summary>
    /// Создаёт и настраивает один FileSystemWatcher для указанной папки.
    /// Возвращает null, если папка пустая или не существует.
    /// </summary>
    private FileSystemWatcher? CreateWatcher(string folderPath)
    {
        // Пропускаем пустые пути
        if (string.IsNullOrWhiteSpace(folderPath))
            return null;

        // Пропускаем несуществующие папки (например, сетевой диск отключён)
        if (!Directory.Exists(folderPath))
            return null;

        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                // Следим только за .mp4 файлами
                Filter = "*.mp4",

                // Следим за всеми подпапками
                IncludeSubdirectories = true,

                // Какие изменения отслеживать:
                // FileName — создание, удаление, переименование файлов
                // Size — изменение размера (файл дописывается после экспорта)
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,

                // Включаем мониторинг
                EnableRaisingEvents = true
            };

            // Подписываемся на все нужные события
            watcher.Created += OnFileChanged;    // Новый файл появился
            watcher.Deleted += OnFileChanged;    // Файл удалён
            watcher.Renamed += OnFileRenamed;    // Файл переименован
            watcher.Changed += OnFileChanged;    // Файл изменился (размер вырос)

            // Если watcher не успевает обработать события — ошибка
            watcher.Error += OnWatcherError;

            return watcher;
        }
        catch (Exception ex)
        {
            LogService.Error($"Не удалось создать FileSystemWatcher для {folderPath}", ex);
            return null;
        }
    }

    /// <summary>
    /// Обработчик событий FileSystemWatcher (Created, Deleted, Changed).
    /// ВАЖНО: этот метод вызывается из фонового потока!
    /// Нельзя напрямую обращаться к UI — используем Dispatcher.
    /// </summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleDebouncedScan();
    }

    /// <summary>
    /// Обработчик переименования файла.
    /// </summary>
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleDebouncedScan();
    }

    /// <summary>
    /// Обработчик ошибок FileSystemWatcher.
    /// Бывает, если буфер переполнен (слишком много событий сразу).
    /// Просто пересканируем — это надёжнее, чем пытаться восстановить.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        LogService.Error("Ошибка FileSystemWatcher", e.GetException());
        ScheduleDebouncedScan();
    }

    /// <summary>
    /// Запланировать сканирование через 500мс (debounce).
    /// 
    /// Зачем debounce? Когда Premiere экспортирует файл, система генерирует
    /// несколько событий подряд: Created, Changed (размер 0), Changed (размер растёт),
    /// Changed (финальный размер). Без debounce мы бы запустили 4 сканирования подряд.
    /// С debounce — ждём 500мс тишины, и только потом сканируем один раз.
    /// </summary>
    private void ScheduleDebouncedScan()
    {
        // Отменяем предыдущий отложенный скан (если был)
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();

        var token = _debounceCts.Token;

        // Запускаем таймер в фоне
        Task.Run(async () =>
        {
            try
            {
                // Ждём 500мс — если за это время придёт новое событие,
                // этот таймер отменится и запустится новый
                await Task.Delay(DebounceDelayMs, token);

                // Время вышло, новых событий не было — запускаем сканирование.
                // ScanFilesAsync() обращается к UI, поэтому вызываем через Dispatcher
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScanFilesAsync();
                });
            }
            catch (TaskCanceledException)
            {
                // Нормально — таймер отменён новым событием, ничего не делаем
            }
        }, token);
    }

    /// <summary>
    /// Вызывается при изменении настроек (пользователь поменял папки поиска).
    /// Пересоздаём watchers для новых папок.
    /// </summary>
    private void OnSettingsChanged()
    {
        SetupFileWatchers();
    }

    /// <summary>
    /// Останавливает и освобождает все FileSystemWatcher.
    /// Вызывается перед пересозданием и при закрытии приложения.
    /// </summary>
    private void DisposeWatchers()
    {
        if (_watcher1 != null)
        {
            _watcher1.EnableRaisingEvents = false;
            _watcher1.Dispose();
            _watcher1 = null;
        }

        if (_watcher2 != null)
        {
            _watcher2.EnableRaisingEvents = false;
            _watcher2.Dispose();
            _watcher2 = null;
        }
    }

    /// <summary>
    /// Освобождение ресурсов. Вызывается из MainWindow при закрытии.
    /// </summary>
    public void Cleanup()
    {
        DisposeWatchers();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _settingsViewModel.SettingsChanged -= OnSettingsChanged;
    }

    // --- Умная навигация по датам (асинхронная) ---

    /// <summary>
    /// Ищет ближайшую дату с файлами в фоновом потоке.
    /// UI не блокируется — кнопки ◀ ▶ отключаются на время поиска,
    /// в статусной строке показывается «Поиск ближайшей даты...»
    /// </summary>
    private async void NavigateToNearestDateAsync(int direction)
    {
        // Защита от повторного запуска
        if (IsNavigating)
            return;

        IsNavigating = true;
        string directionText = direction < 0 ? "назад" : "вперёд";
        StatusMessage = $"Поиск ближайшей даты ({directionText})...";

        var settings = _settingsViewModel.GetSettings();
        DateTime startDate = SelectedDate;

        try
        {
            // Тяжёлая работа — в фоновом потоке (Task.Run)
            // Внутри только чтение файловой системы, никаких обращений к UI
            DateTime? foundDate = await Task.Run(() =>
            {
                DateTime candidate = startDate;

                for (int i = 0; i < MaxSearchDays; i++)
                {
                    candidate = candidate.AddDays(direction);

                    bool hasFiles = _discoveryService.HasFilesForDate(
                        settings.SearchFolder,
                        settings.AdditionalSearchFolder,
                        candidate);

                    if (hasFiles)
                        return candidate; // Нашли — возвращаем дату
                }

                return (DateTime?)null; // Не нашли за 365 дней
            });

            // Обратно в UI-потоке — обновляем свойства
            if (foundDate.HasValue)
            {
                SelectedDate = foundDate.Value;
            }
            else
            {
                // Не нашли — просто сдвигаем на 1 день
                SelectedDate = startDate.AddDays(direction);
                StatusMessage = $"Файлы не найдены за {MaxSearchDays} дней";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка поиска даты: {ex.Message}";
            LogService.Error("Ошибка навигации по датам", ex);
        }
        finally
        {
            IsNavigating = false;
        }
    }

    // ======================================================
    // === Создание проекта ===
    // ======================================================

    private void ExecuteCreateProject()
    {
        var settings = _settingsViewModel.GetSettings();

        var result = _projectService.CreateProject(ProjectName, DateTime.Today, settings);

        if (result.Success)
        {
            StatusMessage = $"✅ {result.Message}";
            ProjectName = string.Empty;

            // Обновляем список проектов — появится новый
            RefreshTodayProjects();

            ScanFilesAsync();
        }
        else
        {
            StatusMessage = $"❌ {result.Message}";
        }
    }

    // ======================================================
    // === Выпадающий список проектов ===
    // ======================================================

    /// <summary>
    /// Обновляет список проектов за сегодняшнюю дату.
    /// Вызывается при старте и после создания нового проекта.
    /// </summary>
    private void RefreshTodayProjects()
    {
        var settings = _settingsViewModel.GetSettings();
        var projects = _projectService.GetTodayProjects(DateTime.Today, settings);
        TodayProjects = new ObservableCollection<string>(projects);
    }

    /// <summary>
    /// Открыть/закрыть выпадающий список проектов.
    /// Перед открытием обновляем список (вдруг папки добавили вручную).
    /// </summary>
    private void ToggleProjectList()
    {
        if (!IsProjectListOpen)
        {
            RefreshTodayProjects();
        }
        IsProjectListOpen = !IsProjectListOpen;
    }

    /// <summary>
    /// Пользователь выбрал проект из списка — генерируем имена.
    /// </summary>
    private void SelectProject(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
            return;

        SelectedProject = projectName;
        IsProjectListOpen = false; // Закрываем выпадающий список
    }

    /// <summary>
    /// Генерируем 4 имени для экспорта по выбранному проекту.
    /// </summary>
    private void UpdateExportNames()
    {
        if (string.IsNullOrEmpty(SelectedProject))
        {
            ExportNames = new ObservableCollection<ExportName>();
            return;
        }

        var names = _projectService.GenerateExportNames(SelectedProject);
        ExportNames = new ObservableCollection<ExportName>(names);
    }

    /// <summary>
    /// Копировать имя в буфер обмена.
    /// </summary>
    private void CopyExportName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        try
        {
            Clipboard.SetText(name);
            StatusMessage = $"📋 Скопировано: {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка копирования в буфер: {ex.Message}";
        }
    }

    // ======================================================
    // === Отмена копирования ===
    // ======================================================

    /// <summary>
    /// Отменяет текущее копирование.
    /// CancellationTokenSource.Cancel() сигнализирует токену —
    /// CopyFileAsync прервёт чтение/запись и удалит недокопированный файл.
    /// </summary>
    public void CancelCopy()
    {
        _copyCts?.Cancel();
    }

    // ======================================================
    // === Копирование файлов ===
    // ======================================================

    public async Task ExecuteCopyAsync(MediaFile file, string destinationKey)
    {
        if (IsCopying)
            return;

        var settings = _settingsViewModel.GetSettings();
        string? efirTime = null;

        // Для Эфир-направления ПАНОРАМА/ДАЙДЖЕСТ — спрашиваем время
        if (destinationKey == "Эфир" &&
            (file.FileType == MediaFileType.Panorama || file.FileType == MediaFileType.Digest))
        {
            string[] timeOptions = file.FileType == MediaFileType.Digest
                ? ["07", "12", "14", "16"]
                : ["18", "20"];

            var dialog = new EfirTimeDialog(timeOptions);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() != true || dialog.SelectedTime == null)
                return;

            efirTime = dialog.SelectedTime;
        }

        // Получаем все направления для этого файла
        var destinations = _copyService.GetDestinations(file, settings, efirTime);

        // Находим нужное направление по ключу (Label)
        var dest = destinations.FirstOrDefault(d => d.Label == destinationKey);
        if (dest == null)
        {
            StatusMessage = $"Направление «{destinationKey}» не найдено";
            return;
        }

        // Проверяем: уже скопировано?
        if (_copyService.IsAlreadyCopied(file.FullPath, dest.DestinationPath))
        {
            if (dest.CopyPathToClipboard)
            {
                Clipboard.SetText(dest.DestinationPath);
                StatusMessage = $"✅ Уже скопировано. Путь в буфере: {dest.DestinationPath}";
            }
            else
            {
                StatusMessage = $"✅ Файл уже скопирован: {Path.GetFileName(dest.DestinationPath)}";
            }
            return;
        }

        // Если файл существует но отличается — спрашиваем перезапись
        if (File.Exists(dest.DestinationPath))
        {
            var result = MessageBox.Show(
                $"Файл уже существует в папке назначения, но отличается.\n\n" +
                $"Перезаписать?\n{dest.DestinationPath}",
                "Подтверждение перезаписи",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                StatusMessage = "Копирование отменено";
                return;
            }
        }

        // Создаём токен отмены для этого копирования
        _copyCts = new CancellationTokenSource();

        // Копируем
        IsCopying = true;
        CopyProgress = 0;
        StatusMessage = $"Копирование: {file.FileName} → {destinationKey}...";

        var progress = new Progress<double>(percent =>
        {
            CopyProgress = percent;
        });

        bool success = await _copyService.CopyFileAsync(
            file.FullPath, dest.DestinationPath, progress, _copyCts.Token);

        bool wasCancelled = _copyCts.IsCancellationRequested;

        // Освобождаем токен
        _copyCts.Dispose();
        _copyCts = null;

        IsCopying = false;
        CopyProgress = 0;

        if (success)
        {
            // Обновляем флаг «скопировано» — кнопка станет залитой
            SetCopiedFlag(file, destinationKey, true);

            if (dest.CopyPathToClipboard)
            {
                Clipboard.SetText(dest.DestinationPath);
                StatusMessage = $"✅ Скопировано! Путь в буфере: {dest.DestinationPath}";
            }
            else
            {
                StatusMessage = $"✅ Скопировано: {file.FileName} → {destinationKey}";
            }
        }
        else if (wasCancelled)
        {
            StatusMessage = $"⛔ Копирование отменено: {file.FileName}";
        }
        else
        {
            StatusMessage = $"❌ Ошибка копирования: {file.FileName} → {destinationKey}";
        }
    }

    // ======================================================
    // === Сканирование (асинхронное) ===
    // ======================================================

    /// <summary>
    /// Асинхронное сканирование файлов.
    /// Тяжёлые операции (обход файловой системы + проверка статусов копирования
    /// по сетевым путям) выполняются в фоновом потоке — UI не зависает.
    /// </summary>
    private async void ScanFilesAsync()
    {
        // Защита от повторного запуска
        if (IsScanning)
            return;

        IsScanning = true;
        StatusMessage = "Сканирование...";

        AppSettings settings = _settingsViewModel.GetSettings();
        // Запоминаем дату ДО await — если пользователь успеет переключить дату,
        // результат старого сканирования не затрёт новый
        DateTime scanDate = SelectedDate;

        try
        {
            // === Фоновый поток: обнаружение файлов ===
            List<FolderGroup> groups = await Task.Run(() =>
                _discoveryService.DiscoverFiles(
                    settings.SearchFolder,
                    settings.AdditionalSearchFolder,
                    scanDate));

            // Если за время сканирования пользователь переключил дату —
            // результат уже не актуален, выбрасываем
            if (scanDate != SelectedDate)
                return;

            // === UI-поток: обновляем список (привязка к интерфейсу) ===
            FolderGroups = new ObservableCollection<FolderGroup>(groups);

            TotalFilesFound = 0;
            foreach (var group in groups)
                TotalFilesFound += group.Files.Count;

            OnPropertyChanged(nameof(IsEmpty));

            if (TotalFilesFound == 0)
            {
                StatusMessage = $"Файлы для {scanDate:dd.MM.yyyy} не найдены";
            }
            else
            {
                string filesWord = GetFilesWord(TotalFilesFound);
                int groupCount = groups.Count;
                string foldersWord = GetFoldersWord(groupCount);
                StatusMessage = $"Найдено {TotalFilesFound} {filesWord} в {groupCount} {foldersWord}";

                // === Фоновый поток: проверка статусов копирования ===
                // Это самая медленная часть — обращения к сетевым дискам
                // по каждому файлу × каждое направление.
                // Собираем данные для фонового потока заранее.
                var fileDestinations = new List<(MediaFile File, List<FileCopyService.CopyDestination> Destinations)>();
                foreach (var group in groups)
                {
                    foreach (var file in group.Files)
                    {
                        var destinations = _copyService.GetDestinations(file, settings);
                        fileDestinations.Add((file, destinations));
                    }
                }

                // Проверяем все статусы в фоновом потоке
                var copyResults = await Task.Run(() =>
                {
                    var results = new List<(MediaFile File, string Label, bool Copied)>();
                    foreach (var (file, destinations) in fileDestinations)
                    {
                        foreach (var dest in destinations)
                        {
                            bool copied = _copyService.IsAlreadyCopied(file.FullPath, dest.DestinationPath);
                            results.Add((file, dest.Label, copied));
                        }
                    }
                    return results;
                });

                // Снова проверяем актуальность даты
                if (scanDate != SelectedDate)
                    return;

                // === UI-поток: ставим флаги (обновляют привязки кнопок) ===
                foreach (var (file, label, copied) in copyResults)
                {
                    SetCopiedFlag(file, label, copied);
                }
            }
        }
        catch (Exception ex)
        {
            // Показываем ошибку только если дата не сменилась
            if (scanDate == SelectedDate)
            {
                StatusMessage = $"Ошибка сканирования: {ex.Message}";
                FolderGroups = new ObservableCollection<FolderGroup>();
                TotalFilesFound = 0;
                OnPropertyChanged(nameof(IsEmpty));
                LogService.Error("Ошибка сканирования файлов", ex);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Устанавливает нужный флаг IsCopiedToXxx по ключу направления.
    /// </summary>
    private static void SetCopiedFlag(MediaFile file, string destinationKey, bool value)
    {
        switch (destinationKey)
        {
            case "Site2 (архив)": file.IsCopiedToSite2 = value; break;
            case "Эфир": file.IsCopiedToEfir = value; break;
            case "Кодер Site": file.IsCopiedToCoder = value; break;
            case "Хранилище": file.IsCopiedToStorage = value; break;
            case "Эфир 25к": file.IsCopiedToEfir25 = value; break;
            case "Кодер 25к": file.IsCopiedToCoder25 = value; break;
            case "Сюжеты": file.IsCopiedToArchive = value; break;
        }
    }

    // --- Вспомогательные ---

    private static string GetFilesWord(int count)
    {
        int lastTwo = count % 100;
        int lastOne = count % 10;
        if (lastTwo >= 11 && lastTwo <= 19) return "файлов";
        if (lastOne == 1) return "файл";
        if (lastOne >= 2 && lastOne <= 4) return "файла";
        return "файлов";
    }

    private static string GetFoldersWord(int count)
    {
        int lastTwo = count % 100;
        int lastOne = count % 10;
        if (lastTwo >= 11 && lastTwo <= 19) return "папках";
        if (lastOne == 1) return "папке";
        return "папках";
    }
}