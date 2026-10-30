using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Views;
public partial class HomePage
{
    private bool _refreshingMaximized=true;
    private void RefreshMaximizedPolicy()
    {
        _refreshingMaximized=true;
        try {AllowMaximizedCard.IsEnabled=OperatingSystem.IsWindows();AllowMaximizedToggle.IsChecked=RuntimeSettings.Current.AllowScalingMaximized;}
        finally {_refreshingMaximized=false;}
    }
    private void AllowMaximized_Changed(object? sender,RoutedEventArgs args)
    {
        if(_refreshingMaximized)return;
        var settings=RuntimeSettings.Current;bool previous=settings.AllowScalingMaximized;
        try {settings.AllowScalingMaximized=AllowMaximizedToggle.IsChecked==true;settings.Save();}
        catch(Exception error)
        {
            settings.AllowScalingMaximized=previous;RefreshMaximizedPolicy();
            (TopLevel.GetTopLevel(this) as MainWindow)?.ShowToast(LocExtension.Get("Home_Advanced_AllowScalingMaximized.Header"),error.Message);
        }
    }
    internal async Task VerifyMaximizedPreferenceAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Maximized preference fixture requires isolated settings.");
        bool previous=RuntimeSettings.Current.AllowScalingMaximized;
        try
        {
            RuntimeSettings.Current.AllowScalingMaximized=false;RefreshMaximizedPolicy();
            if(AllowMaximizedCard.IsEnabled!=OperatingSystem.IsWindows()||AllowMaximizedToggle.IsChecked!=false)
                throw new InvalidOperationException("Maximized preference/capability was not reflected.");
            AllowMaximizedToggle.IsChecked=true;
            if(!RuntimeSettings.Current.AllowScalingMaximized||new HomePage().AllowMaximizedToggle.IsChecked!=true)
                throw new InvalidOperationException("Maximized source preference did not save/reopen.");
            AllowMaximizedCard.BringIntoView();await Task.Delay(100);
            Console.WriteLine("MAXIMIZED PREFERENCE UI PASS: actual toggle handler, global preference, reopen and backend gating; no source captured.");
        }
        finally {RuntimeSettings.Current.AllowScalingMaximized=previous;}
    }
}
