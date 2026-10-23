using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class HomePage
{
    private bool _refreshingToolbar,_toolbarReady;
    private void RefreshToolbar()
    {
        _refreshingToolbar=true;
        var settings=RuntimeSettings.Current.ActiveSettings;
        FullscreenToolbarState.SelectedIndex=(int)settings.FullscreenInitialToolbarState;
        WindowedToolbarState.SelectedIndex=(int)settings.WindowedInitialToolbarState;
        _refreshingToolbar=false;_toolbarReady=true;
    }
    private void ToolbarState_Changed(object? sender,SelectionChangedEventArgs args)
    {
        if(!_toolbarReady || _refreshingToolbar || FullscreenToolbarState is null || WindowedToolbarState is null || FullscreenToolbarState.SelectedIndex<0 || WindowedToolbarState.SelectedIndex<0)return;
        try { ToolbarPreferences.Save(RuntimeSettings.Current.ActiveSettings,FullscreenToolbarState.SelectedIndex,WindowedToolbarState.SelectedIndex); }
        catch(Exception failure) { RefreshToolbar();(TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(Core.Localization.Get("Profile_Toolbar_InitialState.Header"),failure.Message); }
    }
}
