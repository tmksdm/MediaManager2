using MediaManager.Services;
using System.IO;
using System.Windows;

namespace MediaManager.ViewModels;

/// <summary>
/// FileSystemWatcher — автообновление списка файлов и проектов.
/// </summary>
public partial class MainViewModel
{
    // ======================================================
    // === FileSystemWatcher — файлы (.mp4) ===
    // ======================================================

    /// <summary>
    /// Создаёт FileSystemWatcher для папок поиска из настроек.
    /// Следит за появлением / удалением / переименованием .mp4 файлов.
    /// При любом изменении — автоматически обновляет список с debounce.
    /// </summary>
    private void SetupFileWatchers()
    {
        // Сначала убиваем старые watchers (если были)
        DisposeFileWatchers();

        var settings = _settingsViewModel.GetSettings();

        // Watcher для основной папки
        _watcher1 = CreateFileWatcher(settings.SearchFolder);

        // Watcher для дополнительной папки (если указана)
        _watcher2 = CreateFileWatcher(settings.AdditionalSearchFolder);
    }

    /// <summary>
    /// Создаёт и настраивает один FileSystemWatcher для указанной папки.
    /// Возвращает null, если папка пустая или не существует.
    /// </summary>
    private FileSystemWatcher? CreateFileWatcher(string folderPath)
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
    /// Останавливает и освобождает watchers файлов (.mp4).
    /// Вызывается перед пересозданием и при закрытии приложения.
    /// </summary>
    private void DisposeFileWatchers()
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

    // ======================================================
    // === FileSystemWatcher — папки проектов ===
    // ======================================================

    /// <summary>
    /// Создаёт FileSystemWatcher для папки проектов (ProjectBaseFolder).
    /// Следит за созданием / удалением / переименованием ПАПОК (не файлов).
    /// При срабатывании — обновляет выпадающий список проектов с debounce.
    /// 
    /// Не создаёт нагрузки: FileSystemWatcher работает на уровне ОС —
    /// ядро Windows просто отправляет уведомление, когда папка появляется
    /// или исчезает. Никакого периодического сканирования нет.
    /// </summary>
    private void SetupProjectWatcher()
    {
        // Убиваем старый watcher (если был)
        DisposeProjectWatcher();

        var settings = _settingsViewModel.GetSettings();

        if (string.IsNullOrWhiteSpace(settings.ProjectBaseFolder))
            return;

        if (!Directory.Exists(settings.ProjectBaseFolder))
            return;

        try
        {
            _projectWatcher = new FileSystemWatcher(settings.ProjectBaseFolder)
            {
                // Следим за всеми элементами (папки не имеют расширений)
                Filter = "*",

                // Только верхний уровень — проекты лежат прямо в базовой папке
                IncludeSubdirectories = false,

                // Отслеживаем имена папок (создание, удаление, переименование)
                NotifyFilter = NotifyFilters.DirectoryName,

                // Включаем мониторинг
                EnableRaisingEvents = true
            };

            // Подписываемся на события папок
            _projectWatcher.Created += OnProjectFolderChanged;
            _projectWatcher.Deleted += OnProjectFolderChanged;
            _projectWatcher.Renamed += OnProjectFolderRenamed;
            _projectWatcher.Error += OnProjectWatcherError;
        }
        catch (Exception ex)
        {
            LogService.Error($"Не удалось создать FileSystemWatcher для папки проектов: {settings.ProjectBaseFolder}", ex);
        }
    }

    /// <summary>
    /// Обработчик создания/удаления папки в ProjectBaseFolder.
    /// Вызывается из фонового потока — нельзя обращаться к UI напрямую.
    /// </summary>
    private void OnProjectFolderChanged(object sender, FileSystemEventArgs e)
    {
        ScheduleDebouncedProjectRefresh();
    }

    /// <summary>
    /// Обработчик переименования папки в ProjectBaseFolder.
    /// </summary>
    private void OnProjectFolderRenamed(object sender, RenamedEventArgs e)
    {
        ScheduleDebouncedProjectRefresh();
    }

    /// <summary>
    /// Обработчик ошибок watcher-а проектов.
    /// </summary>
    private void OnProjectWatcherError(object sender, ErrorEventArgs e)
    {
        LogService.Error("Ошибка FileSystemWatcher (папка проектов)", e.GetException());
        ScheduleDebouncedProjectRefresh();
    }

    /// <summary>
    /// Запланировать обновление списка проектов через 500мс (debounce).
    /// Аналогично ScheduleDebouncedScan(), но для проектов.
    /// Использует отдельный CancellationTokenSource, чтобы не конфликтовать
    /// с debounce файлового сканирования.
    /// </summary>
    private void ScheduleDebouncedProjectRefresh()
    {
        // Отменяем предыдущий отложенный refresh (если был)
        _projectDebounceCts?.Cancel();
        _projectDebounceCts?.Dispose();
        _projectDebounceCts = new CancellationTokenSource();

        var token = _projectDebounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMs, token);

                // Обновляем список проектов в UI-потоке
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshProjectsForSelectedDate();

                    // Если выбранный проект был удалён — сбрасываем панель экспортных имён
                    if (SelectedProject != null && !TodayProjects.Contains(SelectedProject))
                    {
                        SelectedProject = null;
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // Нормально — отменён новым событием
            }
        }, token);
    }

    /// <summary>
    /// Останавливает и освобождает watcher папки проектов.
    /// </summary>
    private void DisposeProjectWatcher()
    {
        if (_projectWatcher != null)
        {
            _projectWatcher.EnableRaisingEvents = false;
            _projectWatcher.Dispose();
            _projectWatcher = null;
        }
    }

    // ======================================================

    /// <summary>
    /// Вызывается при изменении настроек (пользователь поменял папки поиска).
    /// Пересоздаём watchers для новых папок.
    /// </summary>
    private void OnSettingsChanged()
    {
        SetupFileWatchers();
        SetupProjectWatcher(); // Пересоздаём watcher проектов — путь мог измениться
    }
}