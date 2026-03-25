namespace MediaManager.Models;

/// <summary>
/// Информация о прогрессе копирования: процент, скорость, оставшееся время.
/// Передаётся из FileCopyService через Progress&lt;CopyProgressInfo&gt;.
/// </summary>
public class CopyProgressInfo
{
    /// <summary>Процент выполнения (0–100)</summary>
    public double Percent { get; set; }

    /// <summary>Скорость копирования в байтах/сек (0 если ещё не вычислена)</summary>
    public double BytesPerSecond { get; set; }

    /// <summary>Оставшееся время (null если ещё не вычислено)</summary>
    public TimeSpan? Remaining { get; set; }

    /// <summary>
    /// Форматированная строка скорости: «245 МБ/с» или «12.3 МБ/с»
    /// </summary>
    public string SpeedText
    {
        get
        {
            if (BytesPerSecond <= 0)
                return "";

            double mbPerSec = BytesPerSecond / (1024.0 * 1024.0);

            if (mbPerSec >= 100)
                return $"{mbPerSec:F0} МБ/с";
            if (mbPerSec >= 10)
                return $"{mbPerSec:F1} МБ/с";
            if (mbPerSec >= 1)
                return $"{mbPerSec:F1} МБ/с";

            // Меньше 1 МБ/с — показываем КБ/с
            double kbPerSec = BytesPerSecond / 1024.0;
            return $"{kbPerSec:F0} КБ/с";
        }
    }

    /// <summary>
    /// Форматированная строка оставшегося времени: «~12 сек», «~2 мин», «~1 ч 5 мин»
    /// </summary>
    public string RemainingText
    {
        get
        {
            if (Remaining == null || Remaining.Value.TotalSeconds < 1)
                return "";

            var r = Remaining.Value;

            if (r.TotalSeconds < 60)
                return $"~{(int)r.TotalSeconds} сек";

            if (r.TotalMinutes < 60)
            {
                int min = (int)r.TotalMinutes;
                int sec = r.Seconds;
                return sec > 0 ? $"~{min} мин {sec} сек" : $"~{min} мин";
            }

            int hours = (int)r.TotalHours;
            int mins = r.Minutes;
            return mins > 0 ? $"~{hours} ч {mins} мин" : $"~{hours} ч";
        }
    }
}
