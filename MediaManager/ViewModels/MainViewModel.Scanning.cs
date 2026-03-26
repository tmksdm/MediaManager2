using MediaManager.Models;
using MediaManager.Services;
using System.Collections.ObjectModel;

namespace MediaManager.ViewModels;

/// <summary>
/// Сканирование файлов, проверка статусов копирования, фоновая проверка таймером.
/// </summary>
public partial class MainViewModel
{
    // ======================================================
    // === Фоновая проверка статусов копирования (таймер) ===
    // ======================================================

    /// <summary>
    /// Тихо пересканирует статусы копирования файлов на экране.
    /// Вызывается таймером каждые 30 секунд.
    /// 
    /// Зачем это нужно: после копирования кнопка становится «залитой».
    /// Если кто-то удалит файл из конечной папки — кнопка останется залитой,
    /// потому что приложение об этом не знает. Этот таймер тихо проверяет
    /// все конечные пути и сбрасывает кнопки, если файлы пропали.
    /// 
    /// Не запускается, если:
    /// - нет файлов на экране (нечего проверять)
    /// - идёт полное сканирование (ScanFilesAsync уже проверит статусы)
    /// - идёт копирование (не мешаем, статус обновится после копирования)
    /// - предыдущая проверка ещё не завершилась
    /// </summary>
    private async Task RecheckCopyStatusesAsync()
    {
        // Защита от наложения: если предыдущий тик ещё работает — пропускаем
        if (_isRecheckingStatuses)
            return;

        // Не проверяем, если нет файлов, идёт сканирование или копирование
        if (FolderGroups.Count == 0 || IsScanning || IsCopying)
            return;

        _isRecheckingStatuses = true;

        try
        {
            var settings = _settingsViewModel.GetSettings();

            // Собираем все файлы и их направления для проверки (в UI-потоке)
            var fileDestinations = new List<(MediaFile File, List<FileCopyService.CopyDestination> Destinations)>();
            foreach (var group in FolderGroups)
            {
                foreach (var file in group.Files)
                {
                    var destinations = _copyService.GetDestinations(file, settings);
                    fileDestinations.Add((file, destinations));
                }
            }

            // Проверяем все статусы в фоновом потоке (File.Exists по сети — медленно)
            var results = await Task.Run(() =>
            {
                var list = new List<(MediaFile File, string Label, bool Copied)>();
                foreach (var (file, destinations) in fileDestinations)
                {
                    foreach (var dest in destinations)
                    {
                        bool copied = _copyService.IsAlreadyCopied(file.FullPath, dest.DestinationPath);
                        list.Add((file, dest.Label, copied));
                    }
                }
                return list;
            });

            // Обновляем флаги в UI-потоке (мы уже в UI-потоке после await)
            foreach (var (file, label, copied) in results)
            {
                SetCopiedFlag(file, label, copied);
            }
        }
        catch (Exception ex)
        {
            // Тихо логируем ошибку — не показываем пользователю,
            // чтобы фоновая проверка не мешала работе
            LogService.Error("Ошибка фоновой проверки статусов копирования", ex);
        }
        finally
        {
            _isRecheckingStatuses = false;
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

    // ======================================================
    // === Установка флагов копирования ===
    // ======================================================

    /// <summary>
    /// Устанавливает нужный флаг IsCopiedToXxx по ключу направления.
    /// Ключи берутся из DestinationKeys — единый источник правды.
    /// </summary>
    private static void SetCopiedFlag(MediaFile file, string destinationKey, bool value)
    {
        switch (destinationKey)
        {
            case DestinationKeys.Site2: file.IsCopiedToSite2 = value; break;
            case DestinationKeys.Efir: file.IsCopiedToEfir = value; break;
            case DestinationKeys.CoderSite: file.IsCopiedToCoder = value; break;
            case DestinationKeys.Storage: file.IsCopiedToStorage = value; break;
            case DestinationKeys.Efir25: file.IsCopiedToEfir25 = value; break;
            case DestinationKeys.Coder25: file.IsCopiedToCoder25 = value; break;
            case DestinationKeys.Stories: file.IsCopiedToArchive = value; break;
        }
    }

    // ======================================================
    // === Вспомогательные методы ===
    // ======================================================

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
        if (lastOne >= 2 && lastOne <= 4) return "папках";
        return "папках";
    }
}
