using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Загружает и сохраняет настройки в файл settings.json.
/// Файл хранится в %AppData%/MediaManager/ — у каждого пользователя свой.
/// Это важно, потому что .exe может запускаться из сетевой папки
/// несколькими монтажёрами одновременно — каждый получит свои настройки.
/// </summary>
public static class SettingsService
{
    /// <summary>
    /// Папка для данных приложения: %AppData%/MediaManager/
    /// Например: C:\Users\Вася\AppData\Roaming\MediaManager\
    /// </summary>
    private static readonly string AppDataFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MediaManager");

    /// <summary>Путь к файлу настроек</summary>
    private static readonly string SettingsFilePath =
        Path.Combine(AppDataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>
    /// Загрузить настройки из файла.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка загрузки настроек", ex);
        }

        return new AppSettings();
    }

    /// <summary>
    /// Сохранить настройки в файл.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            // Создаём папку, если её ещё нет (первый запуск)
            Directory.CreateDirectory(AppDataFolder);

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка сохранения настроек", ex);
        }
    }
}
