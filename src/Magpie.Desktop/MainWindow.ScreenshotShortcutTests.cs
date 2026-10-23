using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    internal async Task VerifyScreenshotShortcutEditingAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Shortcut tests require preview mode.");
        var home=(Views.HomePage)_pages["Home"];
        var control=home.FindControl<ShortcutControl>("ScreenshotShortcut")!;
        ShowShortcutDialog(control);
        _shortcutContent!.Capture(new KeyEventArgs { Key=Key.F10,KeyModifiers=KeyModifiers.Control|KeyModifiers.Shift });
        DialogShell.FindControl<Button>("PrimaryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
        if(DialogLayer.IsVisible||RuntimeSettings.Current.ScreenshotShortcut.Trigger!="CTRL+SHIFT+F10"||control.Key!="F10")throw new InvalidOperationException("Screenshot shortcut edit did not save/refresh.");
        var api=new FixtureShortcutApi();_shortcutSessionFactory=()=>new NativeGlobalShortcuts(api);
        try
        {
            await EnableGlobalShortcutsAsync(probe:true);
            if(api.Registered.Count!=4)throw new InvalidOperationException("Screenshot action was not part of system registration.");
            ShowShortcutDialog(control);
            _shortcutContent!.Capture(new KeyEventArgs { Key=Key.A,KeyModifiers=KeyModifiers.Alt|KeyModifiers.Shift });
            DialogShell.FindControl<Button>("PrimaryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            if(!DialogLayer.IsVisible||RuntimeSettings.Current.ScreenshotShortcut.Trigger!="CTRL+SHIFT+F10"||!GlobalShortcutsActive)throw new InvalidOperationException("Duplicate screenshot binding changed settings/registration.");
            _shortcutContent!.Capture(new KeyEventArgs { Key=Key.F9,KeyModifiers=KeyModifiers.Control|KeyModifiers.Alt });
            DialogShell.FindControl<Button>("PrimaryButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            if(DialogLayer.IsVisible||RuntimeSettings.Current.ScreenshotShortcut.Trigger!="CTRL+ALT+F9"||GlobalShortcutsActive||api.Registered.Count!=0)throw new InvalidOperationException("Screenshot edit did not invalidate the old native session.");
            control.BringIntoView();await Task.Delay(60);
            Console.WriteLine("SCREENSHOT SHORTCUT EDIT PASS: actual dialog capture/save, duplicate rejection, fourth system binding and unregister after preference changes; native registration uses an isolated fixture.");
        }
        finally { _shortcutSessionFactory=null;await DisableGlobalShortcutsAsync(); }
    }
}
