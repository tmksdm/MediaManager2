using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaManager.Views;

/// <summary>
/// Диалог выбора времени эфира.
/// Показывает кнопки с вариантами времени (зависят от типа файла).
/// Результат — выбранное время (строка "07", "18" и т.д.) или null если отменили.
/// </summary>
public partial class EfirTimeDialog : Window
{
    /// <summary>Выбранное время. null если пользователь нажал Отмена / закрыл окно.</summary>
    public string? SelectedTime { get; private set; }

    /// <summary>
    /// Создаёт диалог с указанными вариантами времени.
    /// </summary>
    public EfirTimeDialog(string[] timeOptions)
    {
        InitializeComponent();

        foreach (string time in timeOptions)
        {
            var btn = new Button
            {
                Content = time + ":00",
                Width = 80,
                Height = 40,
                Margin = new Thickness(6),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = time
            };

            // Кастомный шаблон для скруглённых углов и hover-эффекта
            var bgColor = (Color)ColorConverter.ConvertFromString("#1565C0");
            var hoverColor = (Color)ColorConverter.ConvertFromString("#1976D2");
            var pressColor = (Color)ColorConverter.ConvertFromString("#0D47A1");

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "border";
            borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(bgColor));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentFactory);
            template.VisualTree = borderFactory;

            // Hover trigger
            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverColor), "border"));
            template.Triggers.Add(hoverTrigger);

            // Press trigger
            var pressTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressColor), "border"));
            template.Triggers.Add(pressTrigger);

            btn.Template = template;
            btn.Foreground = Brushes.White;
            btn.BorderThickness = new Thickness(0);

            btn.Click += TimeButton_Click;
            buttonsPanel.Children.Add(btn);
        }
    }

    private void TimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string time)
        {
            SelectedTime = time;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
