using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Сервис создания проектов.
/// </summary>
public class ProjectCreationService
{
    public class ProjectCreationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ProjectFolderPath { get; set; } = string.Empty;
        public int StubFilesCreated { get; set; }
    }

    /// <summary>
    /// Создать проект: папку с датой, подпапку, шаблон и заглушки .mp4.
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
        string datePrefixCompact = $"{mm}{dd}";

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

        // === 7. Создаём пустые .mp4-заглушки ===
        int stubCount = 0;
        string[] stubFileNames = GenerateStubFileNames(datePrefix, datePrefixCompact, processedName);

        foreach (string stubName in stubFileNames)
        {
            try
            {
                string stubPath = Path.Combine(projectFolderPath, stubName);
                File.Create(stubPath).Dispose();
                stubCount++;
            }
            catch (Exception ex)
            {
                LogService.Error($"Не удалось создать заглушку: {stubName}", ex);
            }
        }

        // === 8. Возвращаем результат ===
        return new ProjectCreationResult
        {
            Success = true,
            Message = $"Проект создан: {subFolderName} ({stubCount} файлов)",
            ProjectFolderPath = projectFolderPath,
            StubFilesCreated = stubCount
        };
    }

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

    private static string[] GenerateStubFileNames(
        string datePrefix, string datePrefixCompact, string processedName)
    {
        string newsName = RemoveLastWord(processedName);

        return
        [
            $"{datePrefix}_{processedName}_.mp4",
            $"НОВОСТИ_{datePrefixCompact}_{newsName}_.mp4",
            $"ПАНОРАМА_18_{datePrefix}_{processedName}_.mp4",
            $"ПАНОРАМА_ДАЙДЖЕСТ_00_{datePrefix}_{processedName}_.mp4"
        ];
    }
}
