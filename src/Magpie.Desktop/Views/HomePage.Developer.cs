using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class HomePage
{
    private bool _refreshingDeveloper=true;
    internal void RefreshDeveloperMode()
    {
        _refreshingDeveloper=true;
        try { DeveloperModeToggle.IsChecked=RuntimeSettings.Current.DeveloperMode;DeveloperOptions.IsVisible=RuntimeSettings.Current.DeveloperMode; }
        finally { _refreshingDeveloper=false; }
    }
    private void DeveloperMode_Changed(object? sender,RoutedEventArgs args)
    {
        if(_refreshingDeveloper||DeveloperModeToggle is null)return;
        var settings=RuntimeSettings.Current;bool previous=settings.DeveloperMode;
        try { settings.DeveloperMode=DeveloperModeToggle.IsChecked==true;settings.Save();RefreshDeveloperMode(); }
        catch(Exception failure)
        {
            settings.DeveloperMode=previous;RefreshDeveloperMode();
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast("Developer mode",failure.Message);
        }
    }
}
