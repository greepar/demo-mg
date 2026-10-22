using Avalonia.Controls;
using Avalonia.Input;
using Magpie.Desktop.Controls;
using Avalonia.Layout;
namespace Magpie.Desktop.Views;
public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();
    public void ShowUpdateState(string state)
    {
        CheckButton.IsEnabled = state != "Checking";
        if (state == "Checking")
        {
            CheckButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new ProgressRing { Foreground = Avalonia.Media.Brushes.White }, new TextBlock { Text = LocExtension.Get("About_Version_CheckingForUpdates.Text") } } };
            UpdateNotice.IsVisible = false;
        }
        else
        {
            CheckButton.Content = LocExtension.Get("About_Version_CheckForUpdates.Text");
            UpdateNotice.ShowState(state);
        }
    }
    private void Version_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow owner) owner.ShowDeveloperOptions();
    }
}
