using System.Windows.Controls;

namespace MediaManager.Views;

/// <summary>
/// Панель журнала копирований.
/// Не содержит своей логики — всё через привязки к MainViewModel.
/// DataContext наследуется от родительского окна.
/// </summary>
public partial class CopyLogPanel : UserControl
{
    public CopyLogPanel()
    {
        InitializeComponent();
    }
}
