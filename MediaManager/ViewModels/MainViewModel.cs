using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using MediaManager.Models;
using MediaManager.Services;
using MediaManager.Views;

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
                ScanFiles();
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
        set { if (_isCopying != value) { _isCopying = value; OnPropertyChanged(); } }
    }

    /// <summary>Прогресс копирования (0–100)</summary>
    private double _copyProgress;
    public double CopyProgress
    {
        get => _copyProgress;
        set { if (Math.Abs(_copyProgress - value) > 0.1) { _copyProgress = value; OnPropertyChanged(); } }
    }

    // --- Команды ---

    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public RelayCommand GoToTodayCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CreateProjectCommand { get; }

    public MainViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;

        // Умная навигация: ищем ближайшую дату с файлами
        NavigateBackCommand = new RelayCommand(_ => NavigateToNearestDate(-1));
        NavigateForwardCommand = new RelayCommand(_ => NavigateToNearestDate(+1));
        GoToTodayCommand = new RelayCommand(_ => SelectedDate = DateTime.Today);
        RefreshCommand = new RelayCommand(_ => ScanFiles());
        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsVisible = !IsSettingsVisible);
        CreateProjectCommand = new RelayCommand(_ => ExecuteCreateProject());

        ScanFiles();
    }

    // --- Умная навигация по датам ---

    /// <summary>
    /// Ищет ближайшую дату с файлами в указанном направлении.
    /// direction = -1 (назад) или +1 (вперёд).
    /// 
    /// Алгоритм:
    /// 1. Начинаем с текущей даты ± 1 день
    /// 2. Проверяем, есть ли файлы для этой даты
    /// 3. Если есть — переключаемся на неё
    /// 4. Если нет — сдвигаемся ещё на 1 день в том же направлении
    /// 5. Максимум 365 попыток (чтобы не искать вечно)
    /// </summary>
    private void NavigateToNearestDate(int direction)
    {
        var settings = _settingsViewModel.GetSettings();
        DateTime candidate = SelectedDate;

        for (int i = 0; i < MaxSearchDays; i++)
        {
            candidate = candidate.AddDays(direction);

            bool hasFiles = _discoveryService.HasFilesForDate(
                settings.SearchFolder,
                settings.AdditionalSearchFolder,
                candidate);

            if (hasFiles)
            {
                SelectedDate = candidate;
                return;
            }
        }

        // Не нашли файлов в пределах 365 дней — просто сдвигаемся на 1 день
        // (чтобы кнопка не «залипла» и пользователь мог продолжить навигацию)
        SelectedDate = SelectedDate.AddDays(direction);
        StatusMessage = $"Файлы не найдены за {MaxSearchDays} дней";
    }

    // --- Создание проекта (STAGE 5) ---

    /// <summary>
    /// Создаёт проект: папки, шаблон .prproj и заглушки .mp4.
    /// После успешного создания — очищает поле ввода и обновляет список файлов.
    /// </summary>
    private void ExecuteCreateProject()
    {
        var settings = _settingsViewModel.GetSettings();

        var result = _projectService.CreateProject(ProjectName, SelectedDate, settings);

        if (result.Success)
        {
            StatusMessage = $"✅ {result.Message}";

            // Очищаем поле ввода — проект создан, имя больше не нужно
            ProjectName = string.Empty;

            // Обновляем список файлов — новые заглушки должны появиться
            ScanFiles();
        }
        else
        {
            StatusMessage = $"❌ {result.Message}";
        }
    }

    // --- Копирование ---

    /// <summary>
    /// Вызывается из code-behind. Параметры: файл и ключ направления.
    /// </summary>
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

        // Копируем
        IsCopying = true;
        CopyProgress = 0;
        StatusMessage = $"Копирование: {file.FileName} → {destinationKey}...";

        var progress = new Progress<double>(percent =>
        {
            CopyProgress = percent;
        });

        bool success = await _copyService.CopyFileAsync(
            file.FullPath, dest.DestinationPath, progress);

        IsCopying = false;
        CopyProgress = 0;

        if (success)
        {
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
        else
        {
            StatusMessage = $"❌ Ошибка копирования: {file.FileName} → {destinationKey}";
        }
    }

    // --- Сканирование ---

    private void ScanFiles()
    {
        try
        {
            StatusMessage = "Сканирование...";

            AppSettings settings = _settingsViewModel.GetSettings();

            List<FolderGroup> groups = _discoveryService.DiscoverFiles(
                settings.SearchFolder,
                settings.AdditionalSearchFolder,
                SelectedDate);

            FolderGroups = new ObservableCollection<FolderGroup>(groups);

            TotalFilesFound = 0;
            foreach (var group in groups)
                TotalFilesFound += group.Files.Count;

            OnPropertyChanged(nameof(IsEmpty));

            if (TotalFilesFound == 0)
                StatusMessage = $"Файлы для {SelectedDateText} не найдены";
            else
            {
                string filesWord = GetFilesWord(TotalFilesFound);
                int groupCount = groups.Count;
                string foldersWord = GetFoldersWord(groupCount);
                StatusMessage = $"Найдено {TotalFilesFound} {filesWord} в {groupCount} {foldersWord}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка сканирования: {ex.Message}";
            FolderGroups = new ObservableCollection<FolderGroup>();
            TotalFilesFound = 0;
            OnPropertyChanged(nameof(IsEmpty));
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
