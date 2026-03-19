using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using MediaManager.Models;
using MediaManager.Services;

namespace MediaManager.ViewModels;

/// <summary>
/// ViewModel для панели настроек.
/// Каждое свойство соответствует одному полю ввода в интерфейсе.
/// При изменении любого поля настройки автоматически сохраняются в файл.
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Событие: настройки изменились (папки поиска и т.д.).
    /// MainViewModel подписывается, чтобы пересоздать FileSystemWatcher.
    /// </summary>
    public event Action? SettingsChanged;

    /// <summary>Объект с текущими настройками</summary>
    private readonly AppSettings _settings;

    // ==============================
    // Свойства — каждое привязано к полю ввода в интерфейсе
    // ==============================

    public string ProjectBaseFolder
    {
        get => _settings.ProjectBaseFolder;
        set
        {
            if (_settings.ProjectBaseFolder != value)
            {
                _settings.ProjectBaseFolder = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string SourceTemplateFile
    {
        get => _settings.SourceTemplateFile;
        set
        {
            if (_settings.SourceTemplateFile != value)
            {
                _settings.SourceTemplateFile = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string SearchFolder
    {
        get => _settings.SearchFolder;
        set
        {
            if (_settings.SearchFolder != value)
            {
                _settings.SearchFolder = value;
                OnPropertyChanged();
                Save();
                // Папка поиска изменилась — нужно пересоздать FileSystemWatcher
                SettingsChanged?.Invoke();
            }
        }
    }

    public string AdditionalSearchFolder
    {
        get => _settings.AdditionalSearchFolder;
        set
        {
            if (_settings.AdditionalSearchFolder != value)
            {
                _settings.AdditionalSearchFolder = value;
                OnPropertyChanged();
                Save();
                // Дополнительная папка поиска изменилась — пересоздать FileSystemWatcher
                SettingsChanged?.Invoke();
            }
        }
    }

    public string Site2Archive
    {
        get => _settings.Site2Archive;
        set
        {
            if (_settings.Site2Archive != value)
            {
                _settings.Site2Archive = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string EfirPanorama
    {
        get => _settings.EfirPanorama;
        set
        {
            if (_settings.EfirPanorama != value)
            {
                _settings.EfirPanorama = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string CoderSite
    {
        get => _settings.CoderSite;
        set
        {
            if (_settings.CoderSite != value)
            {
                _settings.CoderSite = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string NewsStorage
    {
        get => _settings.NewsStorage;
        set
        {
            if (_settings.NewsStorage != value)
            {
                _settings.NewsStorage = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string NewsEfir25
    {
        get => _settings.NewsEfir25;
        set
        {
            if (_settings.NewsEfir25 != value)
            {
                _settings.NewsEfir25 = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string Coder25
    {
        get => _settings.Coder25;
        set
        {
            if (_settings.Coder25 != value)
            {
                _settings.Coder25 = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    public string ArchiveStories
    {
        get => _settings.ArchiveStories;
        set
        {
            if (_settings.ArchiveStories != value)
            {
                _settings.ArchiveStories = value;
                OnPropertyChanged();
                Save();
            }
        }
    }

    // ==============================
    // Команды для кнопок "Обзор..."
    // ==============================

    public RelayCommand BrowseProjectBaseFolderCommand { get; }
    public RelayCommand BrowseSourceTemplateFileCommand { get; }
    public RelayCommand BrowseSearchFolderCommand { get; }
    public RelayCommand BrowseAdditionalSearchFolderCommand { get; }
    public RelayCommand BrowseSite2ArchiveCommand { get; }
    public RelayCommand BrowseEfirPanoramaCommand { get; }
    public RelayCommand BrowseCoderSiteCommand { get; }
    public RelayCommand BrowseNewsStorageCommand { get; }
    public RelayCommand BrowseNewsEfir25Command { get; }
    public RelayCommand BrowseCoder25Command { get; }
    public RelayCommand BrowseArchiveStoriesCommand { get; }

    // ==============================
    // Конструктор
    // ==============================

    public SettingsViewModel()
    {
        // Загружаем настройки из файла (или значения по умолчанию)
        _settings = SettingsService.Load();

        // Создаём команды для каждой кнопки "Обзор..."
        // Для папок — открывается диалог выбора папки
        // Для файла шаблона — диалог выбора файла
        BrowseProjectBaseFolderCommand = new RelayCommand(_ => BrowseFolder(v => ProjectBaseFolder = v));
        BrowseSourceTemplateFileCommand = new RelayCommand(_ => BrowseFile(v => SourceTemplateFile = v));
        BrowseSearchFolderCommand = new RelayCommand(_ => BrowseFolder(v => SearchFolder = v));
        BrowseAdditionalSearchFolderCommand = new RelayCommand(_ => BrowseFolder(v => AdditionalSearchFolder = v));
        BrowseSite2ArchiveCommand = new RelayCommand(_ => BrowseFolder(v => Site2Archive = v));
        BrowseEfirPanoramaCommand = new RelayCommand(_ => BrowseFolder(v => EfirPanorama = v));
        BrowseCoderSiteCommand = new RelayCommand(_ => BrowseFolder(v => CoderSite = v));
        BrowseNewsStorageCommand = new RelayCommand(_ => BrowseFolder(v => NewsStorage = v));
        BrowseNewsEfir25Command = new RelayCommand(_ => BrowseFolder(v => NewsEfir25 = v));
        BrowseCoder25Command = new RelayCommand(_ => BrowseFolder(v => Coder25 = v));
        BrowseArchiveStoriesCommand = new RelayCommand(_ => BrowseFolder(v => ArchiveStories = v));
    }

    // ==============================
    // Вспомогательные методы
    // ==============================

    /// <summary>
    /// Сохранить настройки в файл
    /// </summary>
    private void Save()
    {
        SettingsService.Save(_settings);
    }

    /// <summary>
    /// Открыть диалог выбора папки.
    /// Если пользователь выбрал папку — вызвать setter, который обновит свойство.
    /// </summary>
    private static void BrowseFolder(Action<string> setter)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку"
        };

        if (dialog.ShowDialog() == true)
        {
            setter(dialog.FolderName);
        }
    }

    /// <summary>
    /// Открыть диалог выбора файла (для шаблона .prproj).
    /// </summary>
    private static void BrowseFile(Action<string> setter)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл шаблона",
            Filter = "Premiere Pro Project (*.prproj)|*.prproj|Все файлы (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            setter(dialog.FileName);
        }
    }

    /// <summary>
    /// Получить объект настроек (понадобится другим ViewModels)
    /// </summary>
    public AppSettings GetSettings() => _settings;
}