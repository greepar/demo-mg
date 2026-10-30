using Magpie.Desktop.Core;
using Avalonia.Controls;
namespace Magpie.Desktop.Views;
public partial class ProfilePage
{
    internal async Task VerifyCursorSpeedPreferencesAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Speed preference fixture requires isolated settings.");
        bool previous=Settings.AdjustCursorSpeed;
        try
        {
            Settings.AdjustCursorSpeed=true;RefreshSettings();
            if(CursorSpeedCard.IsEnabled!=OperatingSystem.IsWindows()||CursorSpeedToggle.IsChecked!=true)
                throw new InvalidOperationException("Pointer speed preference/capability was not reflected.");
            CursorSpeedToggle.IsChecked=false;
            var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
            if(Settings.AdjustCursorSpeed||reopened.CursorSpeedToggle.IsChecked!=false)throw new InvalidOperationException("Speed preference did not save/reopen.");
            CursorSpeedToggle.IsChecked=true;
            if(!Settings.AdjustCursorSpeed)throw new InvalidOperationException("Speed preference could not be reenabled.");
            CursorSpeedCard.BringIntoView();await Task.Delay(100);
            Console.WriteLine("CURSOR SPEED UI PASS: toggle/reopen/reenable and backend availability; no native parameters changed.");
        }
        finally {Settings.AdjustCursorSpeed=previous;}
    }
    internal async Task VerifyCursorPresentationPreferencesAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Cursor preference fixture requires isolated settings.");
        var previous=(Settings.CursorScaling,Settings.CustomCursorScaleFactor,Settings.CursorInterpolation,Settings.AdjustCursorSpeed);
        try
        {
            Settings.CursorScaling=7;Settings.CustomCursorScaleFactor=1.7f;Settings.CursorInterpolation=1;RefreshSettings();
            if(CursorScaleExpander.IsEnabled!=OperatingSystem.IsWindows()||CursorScale.SelectedIndex!=7||!CursorCustomScale.IsVisible||
                (float)(CursorCustomScale.Value??0)!=1.7f||CursorInterpolation.SelectedIndex!=1)
                throw new InvalidOperationException("Cursor scaling preferences/capabilities were not reflected.");
            CursorScale.SelectedIndex=5;
            if(Settings.CursorScaling!=5||CursorCustomScale.IsVisible)throw new InvalidOperationException("Cursor preset handler failed.");
            CursorScale.SelectedIndex=7;CursorCustomScale.Value=2.3m;CursorInterpolation.SelectedIndex=0;
            if(CursorSpeedCard.IsEnabled!=OperatingSystem.IsWindows())throw new InvalidOperationException("Pointer speed capability differs.");
            CursorSpeedToggle.IsChecked=false;
            if(Settings.AdjustCursorSpeed)throw new InvalidOperationException("Pointer speed handler did not save.");
            if(Settings.CursorScaling!=7||Settings.CustomCursorScaleFactor!=2.3f||Settings.CursorInterpolation!=0)
                throw new InvalidOperationException("Custom cursor scale/interpolation handlers failed.");
            var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
            if(reopened.CursorScale.SelectedIndex!=7||!reopened.CursorCustomScale.IsVisible||
                (float)(reopened.CursorCustomScale.Value??0)!=2.3f||reopened.CursorInterpolation.SelectedIndex!=0||reopened.CursorSpeedToggle.IsChecked!=false)
                throw new InvalidOperationException("Reopening profile lost cursor scaling preferences.");
            CursorScaleExpander.IsExpanded=true;await Task.Delay(100);
            CursorScaleExpander.BringIntoView();await Task.Delay(100);
            Console.WriteLine("CURSOR PRESENTATION UI PASS: real preset/custom/interpolation/speed handlers, backend gating and reopened profile values; isolated preferences only.");
        }
        finally {(Settings.CursorScaling,Settings.CustomCursorScaleFactor,Settings.CursorInterpolation,Settings.AdjustCursorSpeed)=previous;}
    }
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
