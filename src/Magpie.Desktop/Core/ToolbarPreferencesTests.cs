using Avalonia.Controls;
using Avalonia.VisualTree;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Core;

internal static class ToolbarPreferencesTests
{
    internal static async Task VerifyUiAsync(MainWindow window)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Toolbar fixtures require isolated preview mode.");
        ComboBox Find(string name)=>((Control)window.FindControl<ContentControl>("ContentFrame")!.Content!).FindControl<ComboBox>(name)!;
        window.Navigate("Home");await Task.Delay(50);
        Find("FullscreenToolbarState").SelectedIndex=0;Find("WindowedToolbarState").SelectedIndex=1;
        window.Navigate("Defaults");await Task.Delay(50);
        if(Find("FullscreenToolbarState").SelectedIndex!=0 || Find("WindowedToolbarState").SelectedIndex!=1)throw new InvalidOperationException("Defaults did not restore home toolbar preferences.");
        window.EnsurePreviewProfile();window.Navigate("Profile");await Task.Delay(50);
        if(Find("FullscreenToolbarState").SelectedIndex!=0)throw new InvalidOperationException("New application did not copy toolbar preferences.");
        Find("FullscreenToolbarState").SelectedIndex=2;Find("WindowedToolbarState").SelectedIndex=0;
        if(RuntimeSettings.Current.FullscreenInitialToolbarState!=ToolbarState.Off || RuntimeSettings.Current.WindowedInitialToolbarState!=ToolbarState.AlwaysShow)throw new InvalidOperationException("Application toolbar preferences changed defaults.");
        window.Navigate("Home");await Task.Delay(50);
        Find("ActiveProfileSelector").SelectedIndex=1;await Task.Delay(50);
        if(Find("FullscreenToolbarState").SelectedIndex!=2 || Find("WindowedToolbarState").SelectedIndex!=0)throw new InvalidOperationException("Home did not select application toolbar preferences.");
        Find("WindowedToolbarState").SelectedIndex=1;
        if(RuntimeSettings.Current.ActiveSettings.WindowedInitialToolbarState!=ToolbarState.AlwaysShow)throw new InvalidOperationException("Home did not edit the active application's toolbar preference.");
        window.GetVisualDescendants().OfType<SettingsExpander>().Single(e=>e.Header as string==Localization.Get("Profile_Toolbar_InitialState.Header")).IsExpanded=true;
        await Task.Delay(80);
        var selector=Find("FullscreenToolbarState");
        selector.BringIntoView();await Task.Delay(80);
        Console.WriteLine("TOOLBAR PREFERENCES UI PASS: home/default/application controls, copy independence and active-profile change/restore.");
    }
}
