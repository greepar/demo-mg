using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class MainWindow
{
    internal async Task VerifyShortcutEditingAsync(bool duplicate)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Shortcut UI tests require disabled persistence.");
        var home=(Views.HomePage)_pages["Home"];
        var control=home.FindControl<ShortcutControl>("FullscreenShortcut")!;
        string oldKey=RuntimeSettings.Current.FullscreenShortcut.Key,oldModifiers=RuntimeSettings.Current.FullscreenShortcut.Modifiers;
        try
        {
            ShowShortcutDialog(control);
            _shortcutContent!.Capture(new KeyEventArgs { Key=duplicate?Key.Q:Key.F10,KeyModifiers=duplicate?KeyModifiers.Alt|KeyModifiers.Shift:KeyModifiers.Control|KeyModifiers.Meta });
            var button=DialogShell.FindControl<Button>("PrimaryButton");
            if(button is null)throw new InvalidOperationException("Shortcut save button missing.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            if(duplicate)
            {
                if(!DialogLayer.IsVisible||RuntimeSettings.Current.FullscreenShortcut.Key!=oldKey||RuntimeSettings.Current.FullscreenShortcut.Modifiers!=oldModifiers)
                    throw new InvalidOperationException("Duplicate binding was saved or dialog closed.");
                if(!_shortcutContent!.FindControl<Border>("WarningBanner")!.IsVisible)throw new InvalidOperationException("Duplicate shortcut error is missing.");
            }
            else
            {
                if(DialogLayer.IsVisible||RuntimeSettings.Current.FullscreenShortcut.Trigger!="CTRL+LOGO+F10"||control.Key!="F10"||control.Modifiers!="Win,Ctrl")throw new InvalidOperationException("Shortcut editing did not update settings and key visuals.");
                var replacement=new Views.HomePage();
                if(replacement.FindControl<ShortcutControl>("FullscreenShortcut")!.Key!="F10")throw new InvalidOperationException("Recreated home page lost edited shortcut.");
                // Preview attempts must fail before a real portal or dialog is contacted.
                replacement.FindControl<Button>("ShortcutSetupButton")!.IsEnabled=false;
                try { await EnableGlobalShortcutsAsync();throw new InvalidOperationException("Preview invoked system binding."); }catch(InvalidOperationException error)when(error.Message.Contains("preview mode")) { }
            }
            Console.WriteLine("SHORTCUT UI PASS: "+(duplicate?"duplicate action rejected, settings rolled back and dialog error visible":"actual key capture/save, key visuals, recreated page and preview system-dialog guard"));
        }
        finally
        {
            RuntimeSettings.Current.FullscreenShortcut.Key=oldKey;RuntimeSettings.Current.FullscreenShortcut.Modifiers=oldModifiers;
        }
    }
    internal async Task VerifyPortalShortcutUiAsync()
    {
        if(RuntimeSettings.PersistenceEnabled||Environment.GetEnvironmentVariable("MAGPIE_SHORTCUT_PRIVATE_SESSION")!="1")throw new InvalidOperationException("Portal UI fixtures require a private bus and disabled persistence.");
        ShortcutProbeActivations.Clear();
        await EnableGlobalShortcutsAsync(probe:true);
        if(!GlobalShortcutsActive||!GlobalShortcutStatus.Contains("Custom windowed"))throw new InvalidOperationException("The system's actual trigger was not displayed: "+GlobalShortcutStatus);
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(ShortcutProbeActivations.Count<2&&DateTime.UtcNow<deadline)await Task.Delay(25);
        if(!ShortcutProbeActivations.SequenceEqual(new[]{"windowed","windowed"}))throw new InvalidOperationException("UI-thread portal activation routing failed.");
        var home=(Views.HomePage)_pages["Home"];
        if(!home.FindControl<Button>("ShortcutStopButton")!.IsEnabled||home.FindControl<TextBlock>("ShortcutSystemStatus")!.Text!=GlobalShortcutStatus)throw new InvalidOperationException("Home shortcut status/actions did not reflect portal registration.");
        Console.WriteLine("SHORTCUT PORTAL UI PASS: real private D-Bus request/response on the Avalonia UI context, actual system trigger feedback, de-duplicated action dispatch; capture actions recorded without a chooser.");
    }
    internal async Task VerifyNativeShortcutUiAsync(string mode)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Native shortcut fixtures require disabled persistence.");
        var api=new FixtureShortcutApi { FailRegistration=mode=="Conflict"?1:-1 };
        _shortcutSessionFactory=()=>new NativeGlobalShortcuts(api);
        ShortcutProbeActivations.Clear();
        try
        {
            await EnableGlobalShortcutsAsync(probe:true);
            if(mode=="Conflict")
            {
                if(GlobalShortcutsActive||api.Registered.Count!=0||!api.Disposed||!GlobalShortcutStatus.Contains("conflict"))throw new InvalidOperationException("Native conflict did not roll back registration and update UI.");
            }
            else
            {
                if(!GlobalShortcutsActive||api.Registered.Count!=3||!GlobalShortcutStatus.Contains("Fixture ALT+SHIFT+a"))throw new InvalidOperationException("Native registration status missing.");
                int id=api.Registered.Keys.First();api.Emit(id,true);api.Emit(id,true);api.Emit(id,false);api.Emit(id,true);
                if(!ShortcutProbeActivations.SequenceEqual(new[]{"fullscreen","fullscreen"}))throw new InvalidOperationException("Native UI action dispatch is inconsistent.");
                if(mode=="LayoutChanged")
                {
                    api.LayoutIsCurrent=false;((NativeGlobalShortcuts)_globalShortcuts!).CheckLayout();
                    if(GlobalShortcutsActive||api.Registered.Count!=0||!GlobalShortcutStatus.Contains("Keyboard layout changed"))throw new InvalidOperationException("Keyboard layout change did not disable stale bindings and update UI.");
                }
            }
            var home=(Views.HomePage)_pages["Home"];
            if(home.FindControl<TextBlock>("ShortcutSystemStatus")!.Text!=GlobalShortcutStatus)throw new InvalidOperationException("Native status did not reach the homepage.");
            Console.WriteLine("NATIVE SHORTCUT UI PASS: "+mode+" through the shared desktop controller, fixture API only; no system hotkeys registered.");
        }
        finally { _shortcutSessionFactory=null; }
    }
}
