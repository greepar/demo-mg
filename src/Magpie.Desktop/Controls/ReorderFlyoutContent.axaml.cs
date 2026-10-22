using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;
public partial class ReorderFlyoutContent : UserControl
{
    public event Action<int>? MoveRequested;
    public ReorderFlyoutContent() => InitializeComponent();
    public void SetPosition(int index,int count) { MoveUpButton.IsEnabled=index>0;MoveDownButton.IsEnabled=index<count-1; }
    private void MoveUp_Click(object? sender,RoutedEventArgs args)=>MoveRequested?.Invoke(-1);
    private void MoveDown_Click(object? sender,RoutedEventArgs args)=>MoveRequested?.Invoke(1);
}
