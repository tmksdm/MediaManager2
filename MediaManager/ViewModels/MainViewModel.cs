using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaManager.Models;
using MediaManager.Services;

namespace MediaManager.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ==============================
    // Сервисы
    // ==============================

    /// <summary>Сервис поиска файлов</summary>
    private readonly FileDiscoveryService _discoveryService = new();

    /// <summary>ViewModel настроек (чтобы получать текущие пути)</summary>
    private readonly SettingsViewModel _settingsViewModel;

    // ==============================
    // Свойства
    // ==============================

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
                // При смене даты автоматически сканируем файлы
                ScanFiles();
            }
        }
    }

    public string SelectedDateText => _selectedDate.ToString("dd.MM.yyyy");

    private string _projectName = string.Empty;
    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (_projectName != value)
            {
                _projectName = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isSettingsVisible = false;
    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set
        {
            if (_isSettingsVisible != value)
            {
                _isSettingsVisible = value;
                OnPropertyChanged();
            }
        }
    }

    private string _statusMessage = "Готово";
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Список групп файлов для отображения в интерфейсе.
    /// Каждая группа = одна папка с файлами внутри.
    /// </summary>
    private ObservableCollection<FolderGroup> _folderGroups = new();
    public ObservableCollection<FolderGroup> FolderGroups
    {
        get => _folderGroups;
        set
        {
            _folderGroups = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Общее количество найденных файлов (для отображения в статусе).
    /// </summary>
    private int _totalFilesFound;
    public int TotalFilesFound
    {
        get => _totalFilesFound;
        set
        {
            if (_totalFilesFound != value)
            {
                _totalFilesFound = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Показывать ли заглушку "Файлы не найдены".
    /// true, если список пуст.
    /// </summary>
    public bool IsEmpty => FolderGroups.Count == 0;

    /// <summary>
    /// Путь рабочей папки для отображения в шапке.
    /// </summary>
    public string WorkingFolderDisplay => _settingsViewModel.SearchFolder;

    // ==============================
    // Команды
    // ==============================

    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CreateProjectCommand { get; }

    // ==============================
    // Конструктор
    // ==============================

    public MainViewModel(SettingsViewModel settingsViewModel)
    {
        _settingsViewModel = settingsViewModel;

        NavigateBackCommand = new RelayCommand(_ =>
        {
            SelectedDate = SelectedDate.AddDays(-1);
        });

        NavigateForwardCommand = new RelayCommand(_ =>
        {
            SelectedDate = SelectedDate.AddDays(1);
        });

        RefreshCommand = new RelayCommand(_ =>
        {
            ScanFiles();
        });

        ToggleSettingsCommand = new RelayCommand(_ =>
        {
            IsSettingsVisible = !IsSettingsVisible;
        });

        CreateProjectCommand = new RelayCommand(_ =>
        {
            // Пока ничего — потом тут будет создание проекта
        });

        // Первоначальное сканирование при запуске
        ScanFiles();
    }

    // ==============================
    // Сканирование файлов
    // ==============================

    /// <summary>
    /// Сканировать папки и обновить список файлов.
    /// </summary>
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

            // Считаем общее количество файлов во всех группах
            TotalFilesFound = 0;
            foreach (var group in groups)
            {
                TotalFilesFound += group.Files.Count;
            }

            // Обновляем свойство IsEmpty (оно зависит от FolderGroups)
            OnPropertyChanged(nameof(IsEmpty));

            // Обновляем статус
            if (TotalFilesFound == 0)
            {
                StatusMessage = $"Файлы для {SelectedDateText} не найдены";
            }
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

    // ==============================
    // Вспомогательные методы для склонения слов
    // ==============================

    /// <summary>
    /// Склонение слова "файл" по числу: 1 файл, 2 файла, 5 файлов.
    /// </summary>
    private static string GetFilesWord(int count)
    {
        int lastTwo = count % 100;
        int lastOne = count % 10;

        if (lastTwo >= 11 && lastTwo <= 19)
            return "файлов";
        if (lastOne == 1)
            return "файл";
        if (lastOne >= 2 && lastOne <= 4)
            return "файла";
        return "файлов";
    }

    /// <summary>
    /// Склонение слова "папка" по числу: 1 папке, 2 папках, 5 папках.
    /// </summary>
    private static string GetFoldersWord(int count)
    {
        int lastTwo = count % 100;
        int lastOne = count % 10;

        if (lastTwo >= 11 && lastTwo <= 19)
            return "папках";
        if (lastOne == 1)
            return "папке";
        return "папках";
    }
}
