using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;
public partial class ContentDialog : UserControl
{
    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;
    public ContentDialog() => InitializeComponent();
    public void SetContent(string title, Control content) { TitleLabel.Text = title; Body.Content = content; }
    public void EnableSave(bool enabled) => PrimaryButton.IsEnabled = enabled;
    private void Save_Click(object? sender, RoutedEventArgs e) => Confirmed?.Invoke(this, EventArgs.Empty);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);
}
