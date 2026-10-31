using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Magpie.Desktop.Core;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private void RefreshAutomaticScaling()
    {
        AutoScaleCard.IsVisible=_profile is not null;
        AutoScaleCard.IsEnabled=OperatingSystem.IsWindows()&&_profile is {} profile&&WindowsProfileMatching.SupportsDesktopRule(profile);
        AutoScaleSelector.SelectedIndex=(int)Settings.AutoScale;
    }
    private void AutoScale_SelectionChanged(object? sender,SelectionChangedEventArgs args)
    {
        if(!_initialized||_profile is null||AutoScaleSelector.SelectedIndex is <0 or >2)return;
        var previous=Settings.AutoScale;
        try {Settings.AutoScale=(AutoScaleMode)AutoScaleSelector.SelectedIndex;RuntimeSettings.Current.Save();}
        catch(Exception error)
        {
            Settings.AutoScale=previous;_initialized=false;RefreshAutomaticScaling();_initialized=true;
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(LocExtension.Get("Profile_General_AutoScale.Header"),error.Message);
        }
    }
    internal async Task VerifyAutomaticPreferenceAsync()
    {
        if(RuntimeSettings.PersistenceEnabled||_profile is null)throw new InvalidOperationException("Automatic preference fixture requires an isolated application profile.");
        var previous=Settings.AutoScale;
        try
        {
            foreach(int value in new[]{0,2,1})
            {
                AutoScaleSelector.SelectedIndex=value;
                var reopened=new ProfilePage();reopened.ConfigureProfile(_profile);
                if(Settings.AutoScale!=(AutoScaleMode)value||reopened.AutoScaleSelector.SelectedIndex!=value||!AutoScaleCard.IsVisible||
                    AutoScaleCard.IsEnabled!=(OperatingSystem.IsWindows()&&WindowsProfileMatching.SupportsDesktopRule(_profile)))
                    throw new InvalidOperationException("Automatic preference did not save/reopen or respect source capabilities.");
            }
            await Task.Delay(80);AutoScaleCard.BringIntoView();await Task.Delay(80);
            if(AutoScaleCard.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is {} scroll&&AutoScaleCard.TranslatePoint(default,scroll) is {} origin)
            {
                scroll.Offset=new(scroll.Offset.X,Math.Max(0,scroll.Offset.Y+origin.Y-40));await Task.Delay(80);
                if(AutoScaleCard.TranslatePoint(default,scroll) is not {} visible||visible.Y<0||visible.Y+AutoScaleCard.Bounds.Height>scroll.Viewport.Height)
                    throw new InvalidOperationException("Automatic preference was outside the review viewport.");
            }
            Console.WriteLine("AUTOMATIC PREFERENCE UI PASS: actual selection handler, independent stored modes, reopen and capability gating; no foreground query or capture.");
        }
        finally {Settings.AutoScale=previous;}
    }
}
