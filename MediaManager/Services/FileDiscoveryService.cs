using System.Collections.ObjectModel;
using System.IO;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Сканирует указанные папки, находит .mp4 файлы,
/// парсит их имена и возвращает результат, сгруппированный по папкам.
/// </summary>
public class FileDiscoveryService
{
    /// <summary>
    /// Найти все подходящие .mp4 файлы для указанной даты.
    /// </summary>
    /// <param name="searchFolder">Основная папка поиска</param>
    /// <param name="additionalSearchFolder">Дополнительная папка поиска (может быть пустой)</param>
    /// <param name="selectedDate">Выбранная дата для фильтрации</param>
    /// <returns>Список групп (папка → файлы)</returns>
    public List<FolderGroup> DiscoverFiles(
        string searchFolder,
        string additionalSearchFolder,
        DateTime selectedDate)
    {
        // Сюда будем собирать все найденные файлы из всех папок
        var allFiles = new List<MediaFile>();

        // Сканируем основную папку
        if (!string.IsNullOrWhiteSpace(searchFolder))
        {
            var files = ScanFolder(searchFolder, selectedDate);
            allFiles.AddRange(files);
        }

        // Сканируем дополнительную папку (если указана)
        if (!string.IsNullOrWhiteSpace(additionalSearchFolder))
        {
            var files = ScanFolder(additionalSearchFolder, selectedDate);
            allFiles.AddRange(files);
        }

        // Группируем файлы по родительской папке
        var groups = allFiles
            .GroupBy(f => f.ParentFolderPath)  // Группировка по пути папки
            .Select(g => new FolderGroup       // Каждая группа → FolderGroup
            {
                FolderPath = g.Key,
                FolderName = g.First().ParentFolderName,
                Files = new ObservableCollection<MediaFile>(
                    g.OrderBy(f => f.FileType)         // Сортировка: сначала по типу
                     .ThenBy(f => f.FileName))         // потом по имени файла
            })
            .OrderBy(g => g.FolderName) // Группы — по алфавиту имён папок
            .ToList();

        return groups;
    }

    /// <summary>
    /// Быстрая проверка: есть ли хотя бы один .mp4 файл для указанной даты?
    /// Используется для умной навигации (◀/▶) — не нужно строить полный список,
    /// достаточно найти первый подходящий файл.
    /// </summary>
    /// <param name="searchFolder">Основная папка поиска</param>
    /// <param name="additionalSearchFolder">Дополнительная папка поиска</param>
    /// <param name="date">Дата для проверки</param>
    /// <returns>true если есть хотя бы один файл для этой даты</returns>
    public bool HasFilesForDate(
        string searchFolder,
        string additionalSearchFolder,
        DateTime date)
    {
        // Проверяем основную папку
        if (!string.IsNullOrWhiteSpace(searchFolder) && HasFilesInFolder(searchFolder, date))
            return true;

        // Проверяем дополнительную папку
        if (!string.IsNullOrWhiteSpace(additionalSearchFolder) && HasFilesInFolder(additionalSearchFolder, date))
            return true;

        return false;
    }

    /// <summary>
    /// Есть ли в папке хотя бы один .mp4 файл для указанной даты?
    /// Прекращает поиск сразу после первого найденного — это быстрее,
    /// чем сканировать все файлы.
    /// </summary>
    private bool HasFilesInFolder(string folderPath, DateTime date)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return false;

            string[] mp4Files = Directory.GetFiles(
                folderPath, "*.mp4", SearchOption.AllDirectories);

            foreach (string filePath in mp4Files)
            {
                MediaFile? parsed = FileNameParser.TryParse(filePath, date.Year);
                if (parsed != null && parsed.FileDate.Date == date.Date)
                    return true; // Нашли первый — сразу выходим
            }
        }
        catch
        {
            // Папка недоступна — пропускаем
        }

        return false;
    }

    /// <summary>
    /// Сканировать одну папку (с подпапками) и вернуть найденные файлы для указанной даты.
    /// </summary>
    private List<MediaFile> ScanFolder(string folderPath, DateTime selectedDate)
    {
        var result = new List<MediaFile>();

        try
        {
            // Проверяем, что папка существует
            if (!Directory.Exists(folderPath))
            {
                return result;
            }

            // Ищем все .mp4 файлы, включая подпапки (SearchOption.AllDirectories)
            string[] mp4Files = Directory.GetFiles(
                folderPath,
                "*.mp4",
                SearchOption.AllDirectories);

            foreach (string filePath in mp4Files)
            {
                // Пытаемся разобрать имя файла
                MediaFile? parsed = FileNameParser.TryParse(filePath, selectedDate.Year);

                // Если файл распознан И его дата совпадает с выбранной — добавляем
                if (parsed != null && parsed.FileDate.Date == selectedDate.Date)
                {
                    result.Add(parsed);
                }
            }
        }
        catch
        {
            // Папка недоступна (сетевой диск отключён и т.п.) — просто пропускаем
        }

        return result;
    }
}
