using MediaManager.Services;

namespace MediaManager.ViewModels;

/// <summary>
/// Умная навигация по датам (◀ ▶) — поиск ближайшей даты с файлами.
/// </summary>
public partial class MainViewModel
{
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
}