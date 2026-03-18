using System.Windows;
using MediaManager.ViewModels;

namespace MediaManager;

/// <summary>
/// Главное окно приложения.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Подключаем "мозг" (ViewModel) к окну
        DataContext = new MainViewModel();
    }
}
