using System.Text.Json;
namespace Magpie.Desktop.Core;
internal static class GlobalShortcutSettingsTests
{
    internal static void Run()
    {
        var settings=new RuntimeSettings();GlobalShortcutSettings.Validate(settings);
        if(!GlobalShortcutSettings.Definitions(settings).Select(s=>s.PreferredTrigger).SequenceEqual(new[]{"ALT+SHIFT+a","ALT+SHIFT+q","ALT+SHIFT+d","ALT+SHIFT+s"}))throw new InvalidOperationException("Default global shortcut mapping failed.");
        foreach(var (key,expected) in new[]{("D1","1"),("NumPad3","KP_3"),("F12","F12"),("Enter","Return"),("OemPlus","equal"),("OemQuestion","slash"),("PageUp","Prior"),("Space","space")})
            if(new GlobalShortcutBinding { Key=key,Modifiers="Win,Ctrl" }.Trigger!="CTRL+LOGO+"+expected)throw new InvalidOperationException("Shortcut keysym mapping: "+key);
        foreach(var binding in new[]{new GlobalShortcutBinding{Key=null!},new GlobalShortcutBinding{Modifiers=null!},new GlobalShortcutBinding{Key="LeftCtrl"},new GlobalShortcutBinding{Key="MediaPlayPause"},new GlobalShortcutBinding{Key="None"},new GlobalShortcutBinding{Modifiers="Ctrl,Ctrl"},new GlobalShortcutBinding{Modifiers=""},new GlobalShortcutBinding{Modifiers="Bad"}})
            try { _=binding.Trigger;throw new InvalidOperationException("Unsupported binding accepted."); }catch(InvalidDataException) { }
        settings.WindowedShortcut.Key="A";
        try { settings.Validate();throw new InvalidOperationException("Duplicate shortcut actions accepted."); }catch(InvalidDataException) { }
        settings.WindowedShortcut.Key="Q";
        var copy=settings.CopyForProfile();copy.FullscreenShortcut.Key="B";
        if(settings.FullscreenShortcut.Key!="A")throw new InvalidOperationException("Profiles share mutable shortcut settings.");
        copy.ScreenshotShortcut.Key="F9";
        if(settings.ScreenshotShortcut.Key!="S")throw new InvalidOperationException("Profiles share screenshot shortcut storage.");
        settings.ScreenshotShortcut.Key="A";
        try { settings.Validate();throw new InvalidOperationException("Screenshot/fullscreen duplicate binding was accepted."); }catch(InvalidDataException) { }
        settings.ScreenshotShortcut.Key="F9";settings.ScreenshotShortcut.Modifiers="Ctrl,Alt";
        string root=Path.Combine(Path.GetTempPath(),"magpie-shortcut-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            settings.FullscreenShortcut.Key="F10";settings.FullscreenShortcut.Modifiers="Ctrl,Win";string path=Path.Combine(root,"settings.json");settings.WriteFile(path);
            var restored=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;restored.Validate();
            if(restored.FullscreenShortcut.Trigger!="CTRL+LOGO+F10"||restored.WindowedShortcut.Trigger!="ALT+SHIFT+q"||restored.ScreenshotShortcut.Trigger!="CTRL+ALT+F9")throw new InvalidOperationException("Shortcut disk roundtrip failed.");
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        Console.WriteLine("SHORTCUT SETTINGS PASS: XDG keysyms, canonical modifiers, invalid/duplicate rejection, independent profile copy and AOT disk persistence.");
    }
}
