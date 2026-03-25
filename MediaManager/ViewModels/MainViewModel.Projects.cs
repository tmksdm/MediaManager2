using MediaManager.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace MediaManager.ViewModels;

/// <summary>
/// Создание проектов, выпадающий список проектов, экспортные имена.
/// </summary>
public partial class MainViewModel
{
    // ======================================================
    // === Создание проекта ===
    // ======================================================

    private void ExecuteCreateProject()
    {
        var settings = _settingsViewModel.GetSettings();

        // Проект всегда создаётся за ВЫБРАННУЮ дату (а не за сегодня),
        // чтобы пользователь мог создать проект на нужную дату,
        // даже если перешёл на другую через навигацию.
        var result = _projectService.CreateProject(ProjectName, SelectedDate, settings);

        if (result.Success)
        {
            StatusMessage = $"✅ {result.Message}";
            ProjectName = string.Empty;

            // Обновляем список проектов — появится новый
            RefreshProjectsForSelectedDate();

            ScanFilesAsync();

            // Автозапуск созданного .prproj файла в Premiere Pro
            if (!string.IsNullOrEmpty(result.CreatedPrprojPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result.CreatedPrprojPath,
                        UseShellExecute = true // Откроется в Premiere Pro через ассоциацию Windows
                    });
                }
                catch (Exception ex)
                {
                    // Не блокируем работу, если Premiere не запустился —
                    // проект всё равно создан успешно
                    StatusMessage = $"✅ Проект создан, но не удалось открыть .prproj: {ex.Message}";
                    LogService.Error("Ошибка автозапуска .prproj", ex);
                }
            }
        }
        else
        {
            StatusMessage = $"❌ {result.Message}";
        }
    }

    // ======================================================
    // === Выпадающий список проектов ===
    // ======================================================

    /// <summary>
    /// Обновляет список проектов за выбранную дату (SelectedDate).
    /// Вызывается при старте, после создания нового проекта и при смене даты.
    /// </summary>
    private void RefreshProjectsForSelectedDate()
    {
        var settings = _settingsViewModel.GetSettings();
        var projects = _projectService.GetTodayProjects(SelectedDate, settings);
        TodayProjects = new ObservableCollection<string>(projects);
    }

    /// <summary>
    /// Открыть/закрыть выпадающий список проектов.
    /// Перед открытием обновляем список (вдруг папки добавили вручную).
    /// </summary>
    private void ToggleProjectList()
    {
        if (!IsProjectListOpen)
        {
            RefreshProjectsForSelectedDate();
        }
        IsProjectListOpen = !IsProjectListOpen;
    }

    /// <summary>
    /// Пользователь выбрал проект из списка — генерируем имена.
    /// </summary>
    private void SelectProject(string? projectName)
    {
        if (string.IsNullOrEmpty(projectName))
            return;

        SelectedProject = projectName;
        IsProjectListOpen = false; // Закрываем выпадающий список
    }

    /// <summary>
    /// Генерируем 4 имени для экспорта по выбранному проекту.
    /// </summary>
    private void UpdateExportNames()
    {
        if (string.IsNullOrEmpty(SelectedProject))
        {
            ExportNames = new ObservableCollection<ExportName>();
            return;
        }

        var names = _projectService.GenerateExportNames(SelectedProject);
        ExportNames = new ObservableCollection<ExportName>(names);
    }

    /// <summary>
    /// Копировать имя в буфер обмена.
    /// </summary>
    private void CopyExportName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return;

        try
        {
            Clipboard.SetText(name);
            StatusMessage = $"📋 Скопировано: {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Ошибка копирования в буфер: {ex.Message}";
        }
    }
}