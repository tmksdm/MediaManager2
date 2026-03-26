using MediaManager.Models;
using MediaManager.Services;
using MediaManager.Views;
using System.IO;
using System.Windows;

namespace MediaManager.ViewModels;

/// <summary>
/// Очередь копирования, обработка очереди, отмена, журнал.
/// 
/// Принцип работы:
/// 1. Пользователь нажимает кнопку копирования → ExecuteCopyAsync() добавляет задачу в CopyQueue.
/// 2. Если ProcessQueueAsync() ещё не запущен — запускается.
/// 3. ProcessQueueAsync() берёт задачи из начала коллекции одну за другой и копирует.
/// 4. Панель очереди (XAML) показывает все ожидающие задачи с крестиком ✕ для удаления.
/// 5. В статусной строке: «Копирование: 245 МБ/с, осталось ~12 сек».
/// 6. Кнопка «Отмена» в статусной строке — отменяет текущий файл и очищает всю очередь.
/// 
/// CopyQueue — это ObservableCollection (а не Queue), потому что:
/// - нужна привязка к XAML (ItemsSource)
/// - нужно удалять элемент из середины (крестик на конкретной задаче)
/// Первый элемент [0] — текущий копируемый, остальные — ожидающие.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Запущена ли обработка очереди (защита от двойного запуска)</summary>
    private bool _isProcessingQueue;

    /// <summary>Счётчик обработанных задач в текущей сессии очереди (для «3 из 7»)</summary>
    private int _processedCount;

    // ======================================================
    // === Управление очередью ===
    // ======================================================

    /// <summary>
    /// Удаляет конкретную задачу из очереди по её Id.
    /// Вызывается по крестику ✕ в панели очереди.
    /// Нельзя удалить задачу с индексом 0 — она уже копируется.
    /// </summary>
    private void RemoveFromQueue(object? param)
    {
        if (param is not Guid id)
            return;

        for (int i = 0; i < CopyQueue.Count; i++)
        {
            if (CopyQueue[i].Id == id)
            {
                // Нельзя удалить текущую задачу (индекс 0) — она уже копируется.
                // Для неё есть кнопка «Отмена».
                if (i == 0 && _isProcessingQueue)
                {
                    StatusMessage = "Для отмены текущего копирования используйте кнопку «Отмена»";
                    return;
                }

                string display = CopyQueue[i].DisplayText;
                CopyQueue.RemoveAt(i);
                OnPropertyChanged(nameof(HasQueueItems));
                StatusMessage = $"Убрано из очереди: {display}";
                return;
            }
        }
    }

    /// <summary>
    /// Очищает очередь (все ожидающие задачи), но НЕ отменяет текущее копирование.
    /// Оставляет элемент [0], если он сейчас копируется.
    /// </summary>
    private void ClearQueue()
    {
        if (_isProcessingQueue && CopyQueue.Count > 1)
        {
            // Оставляем только текущую задачу (индекс 0)
            int removed = CopyQueue.Count - 1;
            while (CopyQueue.Count > 1)
            {
                CopyQueue.RemoveAt(CopyQueue.Count - 1);
            }
            OnPropertyChanged(nameof(HasQueueItems));
            StatusMessage = $"Очередь очищена. Убрано задач: {removed}";
        }
        else if (!_isProcessingQueue && CopyQueue.Count > 0)
        {
            int removed = CopyQueue.Count;
            CopyQueue.Clear();
            OnPropertyChanged(nameof(HasQueueItems));
            StatusMessage = $"Очередь очищена. Убрано задач: {removed}";
        }
    }

    /// <summary>
    /// Отменяет текущее копирование И очищает всю очередь.
    /// </summary>
    public void CancelCopy()
    {
        // Очищаем всю очередь (включая текущую задачу — она будет прервана)
        CopyQueue.Clear();
        OnPropertyChanged(nameof(HasQueueItems));

        // Отменяем текущее копирование (CopyFileAsync прервётся и удалит недокопированный файл)
        _copyCts?.Cancel();
    }

    // ======================================================
    // === Постановка в очередь ===
    // ======================================================

    /// <summary>
    /// Точка входа: вызывается из code-behind при нажатии кнопки копирования.
    /// Подготавливает задачу (выбор времени Эфир, проверка «уже скопировано»)
    /// и ставит её в очередь. Если обработка не запущена — запускает.
    /// </summary>
    public async Task ExecuteCopyAsync(MediaFile file, string destinationKey)
    {
        var settings = _settingsViewModel.GetSettings();
        string? efirTime = null;

        // Для Эфир-направления ПАНОРАМА/ДАЙДЖЕСТ — спрашиваем время
        if (destinationKey == DestinationKeys.Efir &&
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
                try
                {
                    Clipboard.SetText(dest.DestinationPath);
                    StatusMessage = $"✅ Уже скопировано. Путь в буфере: {dest.DestinationPath}";
                }
                catch
                {
                    StatusMessage = $"✅ Уже скопировано, но не удалось записать путь в буфер";
                }
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

        // Проверяем дубликат: может, эта пара (файл + направление) уже в очереди?
        foreach (var item in CopyQueue)
        {
            if (item.File.FullPath == file.FullPath && item.DestinationKey == destinationKey)
            {
                StatusMessage = $"⏳ Уже в очереди: {file.FileName} → {destinationKey}";
                return;
            }
        }

        // Ставим задачу в очередь
        CopyQueue.Add(new CopyQueueItem
        {
            File = file,
            DestinationKey = destinationKey,
            DestinationPath = dest.DestinationPath,
            CopyPathToClipboard = dest.CopyPathToClipboard
        });
        OnPropertyChanged(nameof(HasQueueItems));

        StatusMessage = $"⏳ В очереди: {file.FileName} → {destinationKey} (всего: {CopyQueue.Count})";

        // Запускаем обработку очереди, если ещё не запущена
        if (!_isProcessingQueue)
        {
            await ProcessQueueAsync();
        }
    }

    // ======================================================
    // === Обработка очереди ===
    // ======================================================

    /// <summary>
    /// Обрабатывает задачи из очереди одну за другой.
    /// Запускается один раз, работает пока очередь не опустеет.
    /// Новые задачи, добавленные во время работы, подхватываются автоматически.
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        if (_isProcessingQueue)
            return;

        _isProcessingQueue = true;
        IsCopying = true;
        _processedCount = 0;

        try
        {
            while (CopyQueue.Count > 0)
            {
                // Текущая задача — всегда первый элемент коллекции
                var item = CopyQueue[0];
                _processedCount++;

                // Общее число = уже обработали + осталось в очереди (включая текущий)
                int totalTasks = _processedCount + CopyQueue.Count - 1;

                // Создаём токен отмены для этого файла
                _copyCts = new CancellationTokenSource();

                CopyProgress = 0;
                CopySpeedText = string.Empty;

                string queueInfo = totalTasks > 1
                    ? $"[{_processedCount}/{totalTasks}] "
                    : "";
                StatusMessage = $"Копирование {queueInfo}{item.File.FileName} → {item.DestinationKey}...";

                // Прогресс теперь передаёт CopyProgressInfo (процент + скорость + время)
                var progress = new Progress<CopyProgressInfo>(info =>
                {
                    CopyProgress = info.Percent;

                    // Формируем текст скорости и оставшегося времени
                    string speed = info.SpeedText;
                    string remaining = info.RemainingText;

                    if (!string.IsNullOrEmpty(speed) && !string.IsNullOrEmpty(remaining))
                    {
                        CopySpeedText = $"{speed}, осталось {remaining}";
                    }
                    else if (!string.IsNullOrEmpty(speed))
                    {
                        CopySpeedText = speed;
                    }
                    else
                    {
                        CopySpeedText = string.Empty;
                    }
                });

                bool success = await _copyService.CopyFileAsync(
                    item.File.FullPath, item.DestinationPath, progress, _copyCts.Token);

                bool wasCancelled = _copyCts.IsCancellationRequested;

                // Освобождаем токен
                _copyCts.Dispose();
                _copyCts = null;

                // Сбрасываем текст скорости после завершения копирования файла
                CopySpeedText = string.Empty;

                // Удаляем обработанную задачу из очереди
                // (она может уже быть удалена через CancelCopy → CopyQueue.Clear)
                if (CopyQueue.Count > 0 && CopyQueue[0].Id == item.Id)
                {
                    CopyQueue.RemoveAt(0);
                    OnPropertyChanged(nameof(HasQueueItems));
                }

                if (success)
                {
                    // Обновляем флаг «скопировано» — кнопка станет залитой
                    SetCopiedFlag(item.File, item.DestinationKey, true);

                    // Записываем в журнал
                    AddLogEntry(item.File.FileName, item.DestinationKey, CopyLogStatus.Success);

                    if (item.CopyPathToClipboard)
                    {
                        try
                        {
                            Clipboard.SetText(item.DestinationPath);
                            StatusMessage = $"✅ Скопировано! Путь в буфере: {item.DestinationPath}";
                        }
                        catch
                        {
                            StatusMessage = $"✅ Скопировано, но не удалось записать путь в буфер";
                        }
                    }
                    else
                    {
                        int remaining = CopyQueue.Count;
                        StatusMessage = remaining > 0
                            ? $"✅ {item.File.FileName} → {item.DestinationKey}. Осталось: {remaining}"
                            : $"✅ Скопировано: {item.File.FileName} → {item.DestinationKey}";
                    }
                }
                else if (wasCancelled)
                {
                    AddLogEntry(item.File.FileName, item.DestinationKey, CopyLogStatus.Cancelled);
                    StatusMessage = $"⛔ Копирование отменено: {item.File.FileName}";
                    // CancelCopy() уже очистил очередь — цикл завершится
                    break;
                }
                else
                {
                    AddLogEntry(item.File.FileName, item.DestinationKey, CopyLogStatus.Error);
                    StatusMessage = $"❌ Ошибка: {item.File.FileName} → {item.DestinationKey}";
                    // При ошибке — продолжаем следующие задачи (не останавливаем очередь)
                }
            }
        }
        finally
        {
            IsCopying = false;
            CopyProgress = 0;
            CopySpeedText = string.Empty;
            _isProcessingQueue = false;
        }

        // Итоговое сообщение, если обработали несколько задач
        if (_processedCount > 1)
        {
            StatusMessage = $"✅ Очередь завершена. Обработано задач: {_processedCount}";
        }
    }

    // ======================================================
    // === Журнал копирований ===
    // ======================================================

    /// <summary>
    /// Добавляет запись в журнал копирований.
    /// Вставляет в начало списка (самое свежее — сверху).
    /// Удаляет старые записи если превышен лимит.
    /// </summary>
    private void AddLogEntry(string fileName, string destination, CopyLogStatus status)
    {
        var entry = new CopyLogEntry
        {
            Timestamp = DateTime.Now,
            FileName = fileName,
            Destination = destination,
            Status = status
        };

        CopyLog.Insert(0, entry);

        // Убираем старые записи, если превышен лимит
        while (CopyLog.Count > MaxLogEntries)
        {
            CopyLog.RemoveAt(CopyLog.Count - 1);
        }

        OnPropertyChanged(nameof(HasLogEntries));
    }
}
