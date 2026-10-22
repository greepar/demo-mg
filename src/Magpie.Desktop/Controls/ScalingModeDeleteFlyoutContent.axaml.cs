using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;
public partial class ScalingModeDeleteFlyoutContent : UserControl
{
    public event EventHandler? Confirmed;
    public ScalingModeDeleteFlyoutContent() => InitializeComponent();
    public void ShowLinkedProfiles(bool visible) => LinkedProfiles.IsVisible = visible;
    private void Delete_Click(object? sender, RoutedEventArgs args) => Confirmed?.Invoke(this, EventArgs.Empty);
}
