using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Сервис создания проектов.
/// Создаёт структуру папок, копирует шаблон .prproj,
/// создаёт пустые .mp4-заглушки с правильными префиксами.
/// </summary>
public class ProjectCreationService
{
    /// <summary>
    /// Результат создания проекта — что получилось и где.
    /// </summary>
    public class ProjectCreationResult
    {
        /// <summary>Успешно ли создан проект</summary>
        public bool Success { get; set; }

        /// <summary>Сообщение для пользователя</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Путь к созданной подпапке проекта</summary>
        public string ProjectFolderPath { get; set; } = string.Empty;

        /// <summary>Количество созданных файлов-заглушек</summary>
        public int StubFilesCreated { get; set; }
    }

    /// <summary>
    /// Создать проект: папку с датой, подпапку, шаблон и заглушки .mp4.
    /// </summary>
    /// <param name="rawName">Имя проекта, введённое пользователем</param>
    /// <param name="date">Дата, для которой создаётся проект</param>
    /// <param name="settings">Настройки приложения (пути)</param>
    /// <returns>Результат с информацией об успехе/ошибке</returns>
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

        // === 2. Обрабатываем имя: убираем лишние пробелы, заменяем пробелы на _ ===
        string processedName = ProcessName(rawName);

        // === 3. Формируем компоненты даты ===
        string mm = date.Month.ToString("D2");
        string dd = date.Day.ToString("D2");
        string dateFolderName = date.ToString("dd.MM.yyyy"); // "16.03.2026"
        string datePrefix = $"{mm}_{dd}";                     // "03_16"
        string datePrefixCompact = $"{mm}{dd}";               // "0316" (для НОВОСТИ)

        // === 4. Создаём папку даты: D:\Projects\ПАНОРАМА\16.03.2026\ ===
        string dateFolderPath = Path.Combine(settings.ProjectBaseFolder, dateFolderName);
        try
        {
            Directory.CreateDirectory(dateFolderPath);
        }
        catch (Exception ex)
        {
            return new ProjectCreationResult
            {
                Success = false,
                Message = $"Не удалось создать папку даты: {ex.Message}"
            };
        }

        // === 5. Создаём подпапку проекта: ...\16.03.2026\03_16_СвалкаНаФилипова_Адаменко\ ===
        string subFolderName = $"{datePrefix}_{processedName}";
        string projectFolderPath = Path.Combine(dateFolderPath, subFolderName);

        // Проверяем, не существует ли уже такая подпапка
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
            return new ProjectCreationResult
            {
                Success = false,
                Message = $"Не удалось создать папку проекта: {ex.Message}"
            };
        }

        // === 6. Копируем шаблон .prproj ===
        // Имя файла шаблона: 03_16_СвалкаНаФилипова_Адаменко.prproj (без _ перед расширением)
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
            }
            catch
            {
                // Ошибку копирования шаблона игнорируем — главное создать заглушки
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
                // Создаём пустой файл (0 байт)
                File.Create(stubPath).Dispose();
                stubCount++;
            }
            catch
            {
                // Если не удалось создать одну заглушку — продолжаем с остальными
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

    /// <summary>
    /// Обработать имя проекта:
    /// - убрать пробелы в начале и конце
    /// - убрать двойные пробелы
    /// - заменить пробелы на подчёркивания
    /// Пример: "  Свалка На Филипова  " → "Свалка_На_Филипова"
    /// </summary>
    private static string ProcessName(string rawName)
    {
        // Убираем пробелы по краям
        string trimmed = rawName.Trim();

        // Разбиваем по пробелам (RemoveEmptyEntries убирает пустые части от двойных пробелов)
        string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Собираем обратно через подчёркивание
        return string.Join("_", words);
    }

    /// <summary>
    /// Убирает последнее слово (часть после последнего _) из имени.
    /// Используется для файла НОВОСТИ — фамилия журналиста не нужна.
    /// 
    /// Примеры:
    ///   "СвалкаНаФилипова_Адаменко" → "СвалкаНаФилипова"
    ///   "СвалкаНаФилипова"          → "СвалкаНаФилипова" (одно слово — не трогаем)
    /// </summary>
    private static string RemoveLastWord(string processedName)
    {
        int lastUnderscore = processedName.LastIndexOf('_');

        // Если подчёркивания нет — значит имя состоит из одного слова, не трогаем
        if (lastUnderscore <= 0)
            return processedName;

        // Берём всё до последнего _
        return processedName[..lastUnderscore];
    }

    /// <summary>
    /// Генерирует массив имён файлов-заглушек для проекта.
    /// 
    /// Реальный пример для datePrefix="03_16", datePrefixCompact="0316",
    /// processedName="СвалкаНаФилипова_Адаменко":
    /// 
    ///   03_16_СвалкаНаФилипова_Адаменко_.mp4                       (АРХИВ)
    ///   НОВОСТИ_0316_СвалкаНаФилипова_.mp4                         (НОВОСТИ — без фамилии)
    ///   ПАНОРАМА_18_03_16_СвалкаНаФилипова_Адаменко_.mp4           (ПАНОРАМА)
    ///   ПАНОРАМА_ДАЙДЖЕСТ_00_03_16_СвалкаНаФилипова_Адаменко_.mp4  (ДАЙДЖЕСТ)
    /// 
    /// Обратите внимание: перед .mp4 всегда стоит завершающий символ _
    /// </summary>
    private static string[] GenerateStubFileNames(
        string datePrefix, string datePrefixCompact, string processedName)
    {
        // Для НОВОСТИ — убираем фамилию (последнее слово после _)
        string newsName = RemoveLastWord(processedName);

        return
        [
            // АРХИВ: 03_16_Имя_Фамилия_.mp4
            $"{datePrefix}_{processedName}_.mp4",

            // НОВОСТИ: НОВОСТИ_0316_Имя_.mp4 (без фамилии)
            $"НОВОСТИ_{datePrefixCompact}_{newsName}_.mp4",

            // ПАНОРАМА: ПАНОРАМА_18_03_16_Имя_Фамилия_.mp4
            $"ПАНОРАМА_18_{datePrefix}_{processedName}_.mp4",

            // ДАЙДЖЕСТ: ПАНОРАМА_ДАЙДЖЕСТ_00_03_16_Имя_Фамилия_.mp4
            $"ПАНОРАМА_ДАЙДЖЕСТ_00_{datePrefix}_{processedName}_.mp4"
        ];
    }
}
