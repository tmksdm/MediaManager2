using MediaManager.Services;
using MediaManager.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MediaManager.Views;

public partial class ChangelogPanel : UserControl
{
    public ChangelogPanel()
    {
        InitializeComponent();

        // Заполняем данные из статического списка
        changelogList.ItemsSource = ChangelogData.GetChangelog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window?.DataContext is MainViewModel vm)
        {
            vm.IsChangelogVisible = false;
        }
    }
}
