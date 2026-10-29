using Magpie.Desktop.Core;
using Avalonia.Controls;
namespace Magpie.Desktop.Views;
public partial class ProfilePage
{
    internal async Task VerifyCursorAutoHidePreferencesAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Cursor preference fixture requires isolated preview settings.");
        var previous=(Settings.AutoHideCursorEnabled,Settings.AutoHideCursorDelay);
        try
        {
            Settings.AutoHideCursorEnabled=true;Settings.AutoHideCursorDelay=.7;RefreshSettings();
            if(CursorAutoHideExpander.IsEnabled!=OperatingSystem.IsWindows()||!CursorAutoHideExpander.IsExpanded||
                CursorAutoHideToggle.IsChecked!=true||CursorAutoHideDelay.Value!=.7)
                throw new InvalidOperationException("Saved cursor preference/capability was not reflected.");
            // Invoke the real control handlers with isolated settings only;
            // this exercises preference editing, not native Linux/macOS input.
            CursorAutoHideToggle.IsChecked=false;CursorAutoHideDelay.Value=1.4;
            if(Settings.AutoHideCursorEnabled||Settings.AutoHideCursorDelay!=1.4||CursorAutoHideExpander.IsExpanded)
                throw new InvalidOperationException("Cursor preference controls did not update settings.");
            CursorAutoHideToggle.IsChecked=true;
            var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
            if(!reopened.CursorAutoHideExpander.IsExpanded||reopened.CursorAutoHideToggle.IsChecked!=true||reopened.CursorAutoHideDelay.Value!=1.4)
                throw new InvalidOperationException("Reopening profile lost cursor preferences.");
            CursorAutoHideExpander.BringIntoView();await Task.Delay(100);
            Console.WriteLine("CURSOR AUTO-HIDE UI PASS: actual preference handlers, reopened values and backend availability; isolated settings, no native input claim.");
        }
        finally {(Settings.AutoHideCursorEnabled,Settings.AutoHideCursorDelay)=previous;}
    }
}
