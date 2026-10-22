using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;
public partial class DeleteConfirmationFlyoutContent : UserControl
{
    public event EventHandler? Confirmed;
    public DeleteConfirmationFlyoutContent() => InitializeComponent();
    public void SetMessage(string message) => MessageLabel.Text = message;
    private void Delete_Click(object? sender, RoutedEventArgs e) => Confirmed?.Invoke(this, EventArgs.Empty);
}
