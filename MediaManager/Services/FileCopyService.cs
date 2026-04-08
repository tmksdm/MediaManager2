using System.IO;
using System.Runtime.InteropServices;
using MediaManager.Models;

namespace MediaManager.Services;

/// <summary>
/// Строит пути назначения для копирования и выполняет само копирование.
/// Использует CopyFileEx (Win32 API) — тот же механизм, что и Проводник Windows.
/// Это решает проблему с Adobe Media Encoder, который подхватывал недокопированные файлы.
/// </summary>
public class FileCopyService
{
    // ========================= Win32 API =========================

    /// <summary>
    /// Флаги для CopyFileEx.
    /// </summary>
    [Flags]
    private enum CopyFileFlags : uint
    {
        COPY_FILE_FAIL_IF_EXISTS = 0x00000001,
        COPY_FILE_NO_BUFFERING = 0x00001000,    // Без буферизации ОС — быстрее для больших файлов
        COPY_FILE_RESTARTABLE = 0x00000002,      // Позволяет возобновление (не нужно, но не мешает)
    }

    /// <summary>
    /// Причина вызова callback.
    /// </summary>
    private enum CopyProgressCallbackReason : uint
    {
        CALLBACK_CHUNK_FINISHED = 0x00000000,
        CALLBACK_STREAM_SWITCH = 0x00000001,
    }

    /// <summary>
    /// Что делать дальше после callback.
    /// </summary>
    private enum CopyProgressResult : uint
    {
        PROGRESS_CONTINUE = 0,
        PROGRESS_CANCEL = 1,
        PROGRESS_STOP = 2,
        PROGRESS_QUIET = 3,
    }

    /// <summary>
    /// Делегат callback прогресса для CopyFileEx.
    /// </summary>
    private delegate CopyProgressResult CopyProgressRoutine(
        long totalFileSize,
        long totalBytesTransferred,
        long streamSize,
        long streamBytesTransferred,
        uint dwStreamNumber,
        CopyProgressCallbackReason dwCallbackReason,
        IntPtr hSourceFile,
        IntPtr hDestinationFile,
        IntPtr lpData);

    /// <summary>
    /// Win32 CopyFileEx — копирует файл так же, как Проводник Windows.
    /// Файл назначения блокируется на уровне ядра ОС до завершения копирования.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CopyFileExW(
        string lpExistingFileName,
        string lpNewFileName,
        CopyProgressRoutine? lpProgressRoutine,
        IntPtr lpData,
        ref int pbCancel,
        uint dwCopyFlags);

    // ========================= Названия месяцев =========================

    private static readonly string[] MonthsTitleCase =
        ["Январь","Февраль","Март","Апрель","Май","Июнь",
         "Июль","Август","Сентябрь","Октябрь","Ноябрь","Декабрь"];

    private static readonly string[] MonthsLowerCase =
        ["январь","февраль","март","апрель","май","июнь",
         "июль","август","сентябрь","октябрь","ноябрь","декабрь"];

    private static readonly string[] MonthsUpperCase =
        ["ЯНВАРЬ","ФЕВРАЛЬ","МАРТ","АПРЕЛЬ","МАЙ","ИЮНЬ",
         "ИЮЛЬ","АВГУСТ","СЕНТЯБРЬ","ОКТЯБРЬ","НОЯБРЬ","ДЕКАБРЬ"];

    /// <summary>
    /// Описание одного направления копирования: имя кнопки + путь назначения.
    /// </summary>
    public class CopyDestination
    {
        public string Label { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public bool CopyPathToClipboard { get; set; } = false;
    }

    /// <summary>
    /// Возвращает список направлений копирования для данного файла.
    /// </summary>
    public List<CopyDestination> GetDestinations(
        MediaFile file, AppSettings settings, string? efirTime = null)
    {
        var destinations = new List<CopyDestination>();
        DateTime d = file.FileDate;
        string mm = d.Month.ToString("D2");
        string dd = d.Day.ToString("D2");
        string year = d.Year.ToString();
        string monthTitle = MonthsTitleCase[d.Month - 1];
        string monthLower = MonthsLowerCase[d.Month - 1];
        string monthUpper = MonthsUpperCase[d.Month - 1];

        switch (file.FileType)
        {
            case MediaFileType.Panorama:
            case MediaFileType.Digest:
                {
                    // 1) Site2 архив
                    string site2Dir = Path.Combine(settings.Site2Archive, year, $"{mm}_{monthTitle}", dd);
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Site2,
                        DestinationPath = Path.Combine(site2Dir, file.FileName)
                    });

                    // 2) Эфир
                    string efirFileName = file.FileName;
                    if (!string.IsNullOrEmpty(efirTime))
                    {
                        efirFileName = ReplaceTimeInFileName(file, efirTime);
                    }
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Efir,
                        DestinationPath = Path.Combine(settings.EfirPanorama, efirFileName)
                    });

                    // 3) Кодер Site
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.CoderSite,
                        DestinationPath = Path.Combine(settings.CoderSite, file.FileName)
                    });
                    break;
                }

            case MediaFileType.News:
                {
                    // 1) Хранилище
                    string newsDir = Path.Combine(settings.NewsStorage, year, $"{mm}_{monthLower}");
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Storage,
                        DestinationPath = Path.Combine(newsDir, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 2) Эфир 25к
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Efir25,
                        DestinationPath = Path.Combine(settings.NewsEfir25, file.FileName),
                        CopyPathToClipboard = true
                    });

                    // 3) Кодер 25к
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Coder25,
                        DestinationPath = Path.Combine(settings.Coder25, file.FileName),
                        CopyPathToClipboard = true
                    });
                    break;
                }

            case MediaFileType.Archive:
                {
                    // 1) Сюжеты панорамы
                    string archDir = Path.Combine(settings.ArchiveStories, year, $"{mm}_{monthUpper}");
                    destinations.Add(new CopyDestination
                    {
                        Label = DestinationKeys.Stories,
                        DestinationPath = Path.Combine(archDir, file.FileName)
                    });
                    break;
                }
        }

        return destinations;
    }

    /// <summary>
    /// Проверяет, скопирован ли файл в указанное место.
    /// </summary>
    public bool IsAlreadyCopied(string sourcePath, string destPath)
    {
        try
        {
            if (!File.Exists(destPath))
                return false;

            var srcInfo = new FileInfo(sourcePath);
            var dstInfo = new FileInfo(destPath);

            if (srcInfo.Length != dstInfo.Length)
                return false;

            TimeSpan timeDiff = (srcInfo.LastWriteTime - dstInfo.LastWriteTime).Duration();
            return timeDiff.TotalSeconds <= 2;
        }
        catch (Exception ex)
        {
            LogService.Error($"Ошибка проверки копии: {sourcePath} → {destPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Копирует файл через Win32 CopyFileEx — тот же механизм, что и Проводник Windows.
    /// 
    /// Почему это решает проблему с Adobe Media Encoder:
    /// CopyFileEx выполняет копирование на уровне ядра ОС. Файл назначения
    /// создаётся с эксклюзивной блокировкой — другие процессы (включая AME)
    /// не могут открыть его на чтение, пока копирование не завершено полностью.
    /// Это атомарная операция с точки зрения наблюдателей файловой системы.
    /// 
    /// Прогресс сообщается не чаще 20 раз в секунду (50 мс throttle).
    /// Отмена через CancellationToken → устанавливает флаг pbCancel для ядра.
    /// </summary>
    public async Task<bool> CopyFileAsync(
        string sourcePath,
        string destPath,
        IProgress<CopyProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Создаём папку назначения если не существует
            string? destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            var sourceInfo = new FileInfo(sourcePath);
            long totalBytes = sourceInfo.Length;

            // Для throttle прогресса — не чаще 50 мс
            var lastReport = DateTime.UtcNow;
            const int reportIntervalMs = 50;
            var copyStart = DateTime.UtcNow;

            // Флаг отмены для CopyFileEx (передаётся по ref, ядро проверяет его)
            int cancelFlag = 0;

            // Регистрируем CancellationToken → при отмене ставим флаг
            using var ctReg = cancellationToken.Register(() =>
            {
                Interlocked.Exchange(ref cancelFlag, 1);
            });

            // Callback прогресса, вызывается ядром ОС после каждого записанного блока
            CopyProgressResult ProgressCallback(
                long totalFileSize,
                long totalBytesTransferred,
                long streamSize,
                long streamBytesTransferred,
                uint dwStreamNumber,
                CopyProgressCallbackReason dwCallbackReason,
                IntPtr hSourceFile,
                IntPtr hDestinationFile,
                IntPtr lpData)
            {
                // Проверяем отмену
                if (cancellationToken.IsCancellationRequested)
                    return CopyProgressResult.PROGRESS_CANCEL;

                // Throttle прогресса: не чаще 50 мс
                if (progress != null && totalFileSize > 0)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastReport).TotalMilliseconds >= reportIntervalMs
                        || totalBytesTransferred == totalFileSize)
                    {
                        double elapsedSeconds = (now - copyStart).TotalSeconds;
                        double bytesPerSec = elapsedSeconds > 0.1
                            ? totalBytesTransferred / elapsedSeconds
                            : 0;

                        long remainingBytes = totalFileSize - totalBytesTransferred;
                        TimeSpan? remaining = bytesPerSec > 0
                            ? TimeSpan.FromSeconds(remainingBytes / bytesPerSec)
                            : null;

                        progress.Report(new CopyProgressInfo
                        {
                            Percent = (double)totalBytesTransferred / totalFileSize * 100.0,
                            BytesPerSecond = bytesPerSec,
                            Remaining = remaining
                        });

                        lastReport = now;
                    }
                }

                return CopyProgressResult.PROGRESS_CONTINUE;
            }

            // Выполняем копирование в фоновом потоке, чтобы не блокировать UI
            bool success = await Task.Run(() =>
            {
                // Если файл уже существует — удаляем (CopyFileEx без FAIL_IF_EXISTS перезапишет,
                // но удаление даёт чистый старт)
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                bool result = CopyFileExW(
                    sourcePath,
                    destPath,
                    ProgressCallback,
                    IntPtr.Zero,
                    ref cancelFlag,
                    0   // dwCopyFlags = 0: перезаписывать, с буферизацией ОС
                );

                return result;
            }, cancellationToken);

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();

                // ERROR_REQUEST_ABORTED (1235) или отмена через токен
                if (cancellationToken.IsCancellationRequested || error == 1235)
                {
                    // Удаляем недокопированный файл
                    try { File.Delete(destPath); } catch { }
                    return false;
                }

                // Другая ошибка
                var ex = Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
                LogService.Error(
                    $"CopyFileEx failed (Win32 error {error}): {sourcePath} → {destPath}",
                    ex);
                try { File.Delete(destPath); } catch { }
                return false;
            }

            // Финальный прогресс 100%
            progress?.Report(new CopyProgressInfo
            {
                Percent = 100.0,
                BytesPerSecond = 0,
                Remaining = TimeSpan.Zero
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(destPath); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            try { File.Delete(destPath); } catch { }
            LogService.Error($"Ошибка копирования: {sourcePath} → {destPath}", ex);
            return false;
        }
    }

    /// <summary>
    /// Заменяет время (часы) в имени файла ПАНОРАМЫ / ДАЙДЖЕСТА.
    /// </summary>
    private static string ReplaceTimeInFileName(MediaFile file, string newTime)
    {
        string name = file.FileName;
        string timeFormatted = newTime.PadLeft(2, '0');

        if (file.FileType == MediaFileType.Digest)
        {
            int idx = name.IndexOf("ДАЙДЖЕСТ_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int timeStart = idx + "ДАЙДЖЕСТ_".Length;
                if (timeStart + 2 <= name.Length)
                {
                    name = string.Concat(name.AsSpan(0, timeStart), timeFormatted, name.AsSpan(timeStart + 2));
                }
            }
        }
        else if (file.FileType == MediaFileType.Panorama)
        {
            int idx = name.IndexOf("ПАНОРАМА_", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int timeStart = idx + "ПАНОРАМА_".Length;
                if (timeStart + 2 <= name.Length)
                {
                    name = string.Concat(name.AsSpan(0, timeStart), timeFormatted, name.AsSpan(timeStart + 2));
                }
            }
        }

        return name;
    }
}
