using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private void RefreshToolbar()
    {
        FullscreenToolbarState.SelectedIndex=(int)Settings.FullscreenInitialToolbarState;
        WindowedToolbarState.SelectedIndex=(int)Settings.WindowedInitialToolbarState;
    }
    private void ToolbarState_Changed(object? sender,SelectionChangedEventArgs args)
    {
        if(!_initialized || FullscreenToolbarState.SelectedIndex<0 || WindowedToolbarState.SelectedIndex<0)return;
        try { ToolbarPreferences.Save(Settings,FullscreenToolbarState.SelectedIndex,WindowedToolbarState.SelectedIndex); }
        catch(Exception failure)
        {
            bool initialized=_initialized;_initialized=false;RefreshToolbar();_initialized=initialized;
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(Localization.Get("Profile_Toolbar_InitialState.Header"),failure.Message);
        }
    }
}
