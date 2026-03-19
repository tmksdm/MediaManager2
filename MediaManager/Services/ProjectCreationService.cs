using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Сервис создания проектов.
/// Создаёт папку + .prproj файл (без пустых .mp4).
/// Умеет сканировать папку даты и генерировать имена для экспорта.
/// </summary>
public class ProjectCreationService
{
    public class ProjectCreationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ProjectFolderPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Создать проект: папку с датой, подпапку, шаблон .prproj.
    /// Пустые .mp4-заглушки больше НЕ создаются.
    /// </summary>
    public ProjectCreationResult CreateProject(string rawName, DateTime date, AppSettings settings)
    {
        // === 1. Проверяем, что имя не пустое ===
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return new ProjectCreationResult
            {
                Success = false,
                Message = "Введите имя проекта"
            };
        }

        // === 2. Обрабатываем имя ===
        string processedName = ProcessName(rawName);

        // === 3. Формируем компоненты даты ===
        string mm = date.Month.ToString("D2");
        string dd = date.Day.ToString("D2");
        string dateFolderName = date.ToString("dd.MM.yyyy");
        string datePrefix = $"{mm}_{dd}";

        // === 4. Создаём папку даты ===
        string dateFolderPath = Path.Combine(settings.ProjectBaseFolder, dateFolderName);
        try
        {
            Directory.CreateDirectory(dateFolderPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"Не удалось создать папку даты: {dateFolderPath}", ex);
            return new ProjectCreationResult
            {
                Success = false,
                Message = $"Не удалось создать папку даты: {ex.Message}"
            };
        }

        // === 5. Создаём подпапку проекта ===
        string subFolderName = $"{datePrefix}_{processedName}";
        string projectFolderPath = Path.Combine(dateFolderPath, subFolderName);

        if (Directory.Exists(projectFolderPath))
        {
            return new ProjectCreationResult
            {
                Success = false,
                Message = $"Папка проекта уже существует:\n{projectFolderPath}"
            };
        }

        try
        {
            Directory.CreateDirectory(projectFolderPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"Не удалось создать папку проекта: {projectFolderPath}", ex);
            return new ProjectCreationResult
            {
                Success = false,
                Message = $"Не удалось создать папку проекта: {ex.Message}"
            };
        }

        // === 6. Копируем шаблон .prproj ===
        if (!string.IsNullOrWhiteSpace(settings.SourceTemplateFile))
        {
            try
            {
                if (File.Exists(settings.SourceTemplateFile))
                {
                    string templateFileName = $"{datePrefix}_{processedName}.prproj";
                    string templateDestPath = Path.Combine(projectFolderPath, templateFileName);
                    File.Copy(settings.SourceTemplateFile, templateDestPath);
                }
                else
                {
                    LogService.Error($"Файл шаблона не найден: {settings.SourceTemplateFile}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Ошибка копирования шаблона", ex);
            }
        }

        // === 7. Возвращаем результат ===
        return new ProjectCreationResult
        {
            Success = true,
            Message = $"Проект создан: {subFolderName}",
            ProjectFolderPath = projectFolderPath
        };
    }

    /// <summary>
    /// Получить список проектов (имён подпапок) за указанную дату.
    /// Сканирует папку DD.MM.YYYY внутри базовой папки проектов.
    /// </summary>
    public List<string> GetTodayProjects(DateTime date, AppSettings settings)
    {
        var projects = new List<string>();

        string dateFolderName = date.ToString("dd.MM.yyyy");
        string dateFolderPath = Path.Combine(settings.ProjectBaseFolder, dateFolderName);

        if (!Directory.Exists(dateFolderPath))
            return projects;

        try
        {
            // Получаем все подпапки в папке даты
            var subDirs = Directory.GetDirectories(dateFolderPath);
            foreach (string dir in subDirs)
            {
                string folderName = Path.GetFileName(dir);
                projects.Add(folderName);
            }

            // Сортируем по алфавиту
            projects.Sort();
        }
        catch (Exception ex)
        {
            LogService.Error($"Ошибка сканирования проектов за {dateFolderName}", ex);
        }

        return projects;
    }

    /// <summary>
    /// Генерирует 4 имени файлов для экспорта (без расширения .mp4).
    /// Формат идентичен прежним .mp4-заглушкам, но без расширения.
    /// </summary>
    /// <param name="projectFolderName">Имя подпапки проекта, например "03_19_СвалкаНаФилипова_Адаменко"</param>
    /// <returns>Список из 4 имён с описанием типа</returns>
    public List<ExportName> GenerateExportNames(string projectFolderName)
    {
        var names = new List<ExportName>();

        // Разбираем имя папки: "MM_DD_ИмяПроекта"
        // Первые 5 символов — "MM_DD", остальное после "_" — имя
        if (projectFolderName.Length < 6 || projectFolderName[2] != '_' || projectFolderName[5] != '_')
            return names;

        string mm = projectFolderName[..2];
        string dd = projectFolderName[3..5];
        string processedName = projectFolderName[6..];
        string datePrefixCompact = $"{mm}{dd}";
        string datePrefix = $"{mm}_{dd}";

        string newsName = RemoveLastWord(processedName);

        names.Add(new ExportName
        {
            TypeLabel = "АРХИВ",
            Name = $"{datePrefix}_{processedName}_",
            TypeColor = "#43A047"
        });

        names.Add(new ExportName
        {
            TypeLabel = "НОВОСТИ",
            Name = $"НОВОСТИ_{datePrefixCompact}_{newsName}_",
            TypeColor = "#E53935"
        });

        names.Add(new ExportName
        {
            TypeLabel = "ПАНОРАМА",
            Name = $"ПАНОРАМА_18_{datePrefix}_{processedName}_",
            TypeColor = "#8E24AA"
        });

        names.Add(new ExportName
        {
            TypeLabel = "ДАЙДЖЕСТ",
            Name = $"ПАНОРАМА_ДАЙДЖЕСТ_00_{datePrefix}_{processedName}_",
            TypeColor = "#8E24AA"
        });

        return names;
    }

    // --- Вспомогательные методы ---

    private static string ProcessName(string rawName)
    {
        string trimmed = rawName.Trim();
        string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", words);
    }

    private static string RemoveLastWord(string processedName)
    {
        int lastUnderscore = processedName.LastIndexOf('_');
        if (lastUnderscore <= 0)
            return processedName;
        return processedName[..lastUnderscore];
    }
}

/// <summary>
/// Одно имя файла для экспорта (без .mp4).
/// Отображается в панели под строкой создания проекта.
/// </summary>
public class ExportName
{
    /// <summary>Тип файла для бейджа: АРХИВ, НОВОСТИ, ПАНОРАМА, ДАЙДЖЕСТ</summary>
    public string TypeLabel { get; set; } = string.Empty;

    /// <summary>Имя для копирования в буфер (без .mp4)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Цвет бейджа</summary>
    public string TypeColor { get; set; } = "#9E9E9E";
}
