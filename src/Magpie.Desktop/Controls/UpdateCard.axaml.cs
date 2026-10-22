using Avalonia.Controls;
using Avalonia.Media;
namespace Magpie.Desktop.Controls;
public partial class UpdateCard : UserControl
{
    public UpdateCard()
    {
        InitializeComponent();
        Notice.PropertyChanged += (_, args) => { if (args.Property == InfoBar.IsOpenProperty) IsVisible = Notice.IsOpen; };
    }
    // Visual state only: no HTTP, installer or update service is invoked.
    public void ShowState(string state, bool home = false)
    {
        IsVisible = true;
        Notice.IsOpen = true;
        Notice.Severity = state == "Updated" ? "Success" : state == "Error" ? "Error" : "Informational";
        Notice.Title = state switch
        {
            "Updated" => LocExtension.Get("About_Version_UpdateToDate.Title"),
            "Error" => LocExtension.Get("About_Version_CheckForUpdatesFailed.Title"),
            _ => LocExtension.Get("About_Version_UpdateCard_Title").Replace("{}", "0.14.0")
        };
        Details.IsVisible = state is not ("Updated" or "Error");
        ProgressPanel.IsVisible = state is "Downloading" or "Installing" or "DownloadFailed";
        ProgressLabel.Text = LocExtension.Get("About_Version_UpdateToDate_" + (state == "DownloadFailed" ? "DownloadFailed" : state == "Installing" ? "Installing" : "Downloading") + ".Text");
        Progress.Foreground = state == "DownloadFailed" ? Brush.Parse("#C42B1C") : Brush.Parse("#0067C0");
        Progress.Value = state == "Installing" ? 1 : .6;
        DownloadButton.IsVisible = state is "Available" or "DownloadFailed";
        DownloadButton.Content = LocExtension.Get(state == "DownloadFailed" ? "About_Version_UpdateCard_Retry.Content" : "About_Version_UpdateCard_DownloadAndInstall.Content");
        CancelButton.IsVisible = state is "Downloading" or "DownloadFailed";
        Notice.IsClosable = state is not ("Downloading" or "Installing");
        AutoCheck.IsVisible = home;
        RemindLaterButton.IsVisible = home;
    }
}
