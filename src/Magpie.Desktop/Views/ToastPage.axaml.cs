using Avalonia.Controls;
namespace Magpie.Desktop.Views;
public partial class ToastPage : UserControl
{
    public ToastPage() => InitializeComponent();
    public void SetMessage(string title, string message, bool showLogo = true)
    {
        TitleLabel.Text = title; TitleLabel.IsVisible = !string.IsNullOrEmpty(title);
        MessageLabel.Text = message; Logo.IsVisible = showLogo;
    }
}
