using System.IO;

namespace MediaManager.Services;

/// <summary>
/// Простой сервис логирования в текстовый файл.
/// Файл log.txt создаётся рядом с .exe программы.
/// 
/// Каждая запись содержит дату/время, уровень (INFO/ERROR) и текст.
/// Файл автоматически очищается, если превышает 5 МБ.
/// </summary>
public static class LogService
{
    /// <summary>Путь к файлу лога (рядом с .exe)</summary>
    private static readonly string LogFilePath =
        Path.Combine(AppContext.BaseDirectory, "log.txt");

    /// <summary>Максимальный размер лога — 5 МБ. При превышении файл очищается.</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Блокировка для потокобезопасной записи.
    /// Несколько потоков могут одновременно попытаться записать в лог —
    /// lock гарантирует, что записи не перемешаются.
    /// </summary>
    private static readonly object _lock = new();

    /// <summary>
    /// Записать информационное сообщение.
    /// </summary>
    public static void Info(string message)
    {
        Write("INFO", message);
    }

    /// <summary>
    /// Записать ошибку (только текст).
    /// </summary>
    public static void Error(string message)
    {
        Write("ERROR", message);
    }

    /// <summary>
    /// Записать ошибку вместе с исключением (stack trace).
    /// Это самый полезный вариант — сохраняет полную информацию об ошибке.
    /// </summary>
    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}: {ex.Message}\n    {ex.StackTrace}");
    }

    /// <summary>
    /// Записать одну строку в лог-файл.
    /// Формат: [2026-03-18 14:30:05] ERROR Текст ошибки
    /// </summary>
    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                // Проверяем размер файла — если слишком большой, очищаем
                TrimIfNeeded();

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = $"[{timestamp}] {level} {message}{Environment.NewLine}";

                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Если не удалось записать в лог — молча игнорируем.
            // Нельзя бросать исключения из логирования, иначе программа упадёт.
        }
    }

    /// <summary>
    /// Если файл лога превышает максимальный размер — удаляем его.
    /// Начнётся новый лог с чистого листа.
    /// </summary>
    private static void TrimIfNeeded()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var fileInfo = new FileInfo(LogFilePath);
                if (fileInfo.Length > MaxLogSizeBytes)
                {
                    File.Delete(LogFilePath);
                }
            }
        }
        catch
        {
            // Не критично
        }
    }
}
