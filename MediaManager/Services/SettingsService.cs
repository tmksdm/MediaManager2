using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Загружает и сохраняет настройки в файл settings.json.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsFilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

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
                    LogService.Info("Настройки загружены из settings.json");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка загрузки настроек", ex);
        }

        LogService.Info("Используются настройки по умолчанию");
        return new AppSettings();
    }

    /// <summary>
    /// Сохранить настройки в файл.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            LogService.Error("Ошибка сохранения настроек", ex);
        }
    }
}
