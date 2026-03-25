using System.Windows.Controls;

namespace MediaManager.Views;

/// <summary>
/// Панель очереди копирования.
/// Не содержит своей логики — всё через привязки к MainViewModel.
/// DataContext наследуется от родительского окна.
/// </summary>
public partial class CopyQueuePanel : UserControl
{
    public CopyQueuePanel()
    {
        InitializeComponent();
    }
}
