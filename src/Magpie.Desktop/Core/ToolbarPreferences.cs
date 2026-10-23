namespace Magpie.Desktop.Core;

internal static class ToolbarPreferences
{
    internal static void Save(RuntimeSettings settings,int fullscreen,int windowed)
    {
        var previous=(settings.FullscreenInitialToolbarState,settings.WindowedInitialToolbarState);
        try
        {
            settings.FullscreenInitialToolbarState=(ToolbarState)fullscreen;settings.WindowedInitialToolbarState=(ToolbarState)windowed;
            RuntimeSettings.Current.Save();
        }
        catch { (settings.FullscreenInitialToolbarState,settings.WindowedInitialToolbarState)=previous;throw; }
    }
}
