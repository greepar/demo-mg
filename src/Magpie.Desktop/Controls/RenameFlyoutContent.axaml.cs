using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;
public partial class RenameFlyoutContent : UserControl
{
    public event EventHandler? Confirmed;
    public RenameFlyoutContent() => InitializeComponent();
    private string _originalName = "";
    public string Value { get => NameInput.Text?.Trim() ?? ""; set { _originalName = value; NameInput.Text = value; ConfirmButton.IsEnabled = false; } }
    private void Name_Changed(object? sender, TextChangedEventArgs args)
    {
        if (ConfirmButton is not null) ConfirmButton.IsEnabled = !string.IsNullOrWhiteSpace(NameInput.Text) && NameInput.Text.Trim() != _originalName;
    }
    private void Confirm_Click(object? sender, RoutedEventArgs args) => Confirmed?.Invoke(this, EventArgs.Empty);
}
