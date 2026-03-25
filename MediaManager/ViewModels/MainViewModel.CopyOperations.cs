using MediaManager.Models;
using MediaManager.Services;
using MediaManager.Views;
using System.IO;
using System.Windows;

namespace MediaManager.ViewModels;

/// <summary>
/// Копирование файлов, отмена копирования, журнал копирований.
/// </summary>
public partial class MainViewModel
{
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

            // Записываем в журнал
            AddLogEntry(file.FileName, destinationKey, CopyLogStatus.Success);

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
            // Записываем в журнал
            AddLogEntry(file.FileName, destinationKey, CopyLogStatus.Cancelled);

            StatusMessage = $"⛔ Копирование отменено: {file.FileName}";
        }
        else
        {
            // Записываем в журнал
            AddLogEntry(file.FileName, destinationKey, CopyLogStatus.Error);

            StatusMessage = $"❌ Ошибка копирования: {file.FileName} → {destinationKey}";
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