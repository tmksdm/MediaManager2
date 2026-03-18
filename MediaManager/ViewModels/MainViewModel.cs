using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MediaManager.ViewModels;

/// <summary>
/// Главная ViewModel — управляет состоянием основного окна.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    // ===== Событие, которое сообщает интерфейсу об изменениях =====
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ===== Текущая выбранная дата =====
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
            }
        }
    }

    /// <summary>
    /// Дата в формате "dd.MM.yyyy" для отображения в интерфейсе.
    /// </summary>
    public string SelectedDateText => _selectedDate.ToString("dd.MM.yyyy");

    // ===== Имя проекта (вводит пользователь) =====
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

    // ===== Видимость панели настроек =====
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

    // ===== Текст статусной строки =====
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

    // ===== Команды для кнопок =====
    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }
    public RelayCommand CreateProjectCommand { get; }

    // ===== Конструктор =====
    public MainViewModel()
    {
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
            // Пока ничего — потом тут будет сканирование файлов
        });

        ToggleSettingsCommand = new RelayCommand(_ =>
        {
            IsSettingsVisible = !IsSettingsVisible;
        });

        CreateProjectCommand = new RelayCommand(_ =>
        {
            // Пока ничего — потом тут будет создание проекта
        });
    }
}
