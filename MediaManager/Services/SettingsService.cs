using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Загружает и сохраняет настройки в файл settings.json
/// рядом с исполняемым файлом программы.
/// </summary>
public static class SettingsService
{
    /// <summary>
    /// Полный путь к файлу settings.json.
    /// AppContext.BaseDirectory — папка, где лежит .exe нашей программы.
    /// </summary>
    private static readonly string SettingsFilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>
    /// Параметры сериализации JSON:
    /// - WriteIndented: красивый отступ для читаемости файла
    /// - Encoder: сохранять кириллицу как есть, а не как \u0410\u041F...
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>
    /// Загрузить настройки из файла.
    /// Если файла нет — возвращает объект с настройками по умолчанию.
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
        catch
        {
            // Если файл повреждён — просто используем настройки по умолчанию
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
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ошибку сохранения пока игнорируем — позже добавим логирование
        }
    }
}
