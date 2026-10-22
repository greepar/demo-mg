using Avalonia.Input;
namespace Magpie.Desktop.Core;

internal sealed class GlobalShortcutBinding
{
    public string Key { get; set; }="A";
    public string Modifiers { get; set; }="Alt,Shift";
    internal KeyModifiers AvaloniaModifiers=>Modifiers.Split(',').Aggregate(KeyModifiers.None,(value,modifier)=>value|(modifier switch { "Ctrl"=>KeyModifiers.Control,"Alt"=>KeyModifiers.Alt,"Shift"=>KeyModifiers.Shift,"Win"=>KeyModifiers.Meta,_=>KeyModifiers.None }));
    internal string Trigger
    {
        get
        {
            if(string.IsNullOrEmpty(Key)||Key.Length>40||Modifiers is null||Modifiers.Length>40)throw new InvalidDataException("Invalid shortcut data.");
            if(!Enum.TryParse<Key>(Key,out var key)||key==Avalonia.Input.Key.None)throw new InvalidDataException("Unsupported shortcut key.");
            string symbol=Key.Length==1&&char.IsAsciiLetter(Key[0]) ? Key.ToLowerInvariant() : Key;
            if(Key.Length==2&&Key[0]=='D'&&char.IsAsciiDigit(Key[1]))symbol=Key[1..];
            else if(Key.StartsWith("NumPad",StringComparison.Ordinal)&&Key.Length==7&&char.IsAsciiDigit(Key[6]))symbol="KP_"+Key[6];
            else symbol=Key switch { "Enter"=>"Return","Back"=>"BackSpace","PageUp"=>"Prior","PageDown"=>"Next","Space"=>"space","OemPlus"=>"equal","OemMinus"=>"minus","OemComma"=>"comma","OemPeriod"=>"period","OemQuestion"=>"slash","OemSemicolon"=>"semicolon","OemQuotes"=>"apostrophe","OemOpenBrackets"=>"bracketleft","OemCloseBrackets"=>"bracketright","OemPipe"=>"backslash","OemTilde"=>"grave","Multiply"=>"KP_Multiply","Add"=>"KP_Add","Subtract"=>"KP_Subtract","Divide"=>"KP_Divide","Decimal"=>"KP_Decimal",_=>symbol };
            // Modifiers alone and platform-specific/media keys need a real keysym map.
            bool supported=Key.Length==1&&char.IsAsciiLetter(Key[0]) || Key.Length==2&&Key[0]=='D'&&char.IsAsciiDigit(Key[1]) ||
                Key.StartsWith("NumPad",StringComparison.Ordinal)&&Key.Length==7&&char.IsAsciiDigit(Key[6]) ||
                Key.StartsWith('F')&&int.TryParse(Key.AsSpan(1),out int fn)&&fn is >=1 and <=24 ||
                new[]{"Enter","Back","PageUp","PageDown","Space","Tab","Escape","Insert","Delete","Home","End","Left","Right","Up","Down","OemPlus","OemMinus","OemComma","OemPeriod","OemQuestion","OemSemicolon","OemQuotes","OemOpenBrackets","OemCloseBrackets","OemPipe","OemTilde","Multiply","Add","Subtract","Divide","Decimal","Pause","Print","Scroll","NumLock","CapsLock"}.Contains(Key);
            if(!supported)throw new InvalidDataException("This key has no supported global shortcut mapping.");
            symbol=Key switch { "Print"=>"Print","Scroll"=>"Scroll_Lock","NumLock"=>"Num_Lock","CapsLock"=>"Caps_Lock",_=>symbol };
            var modifiers=Modifiers.Split(',',StringSplitOptions.RemoveEmptyEntries);
            if(modifiers.Length==0||modifiers.Distinct().Count()!=modifiers.Length||modifiers.Any(m=>m is not ("Ctrl" or "Alt" or "Shift" or "Win")))throw new InvalidDataException("Invalid shortcut modifiers.");
            return string.Join('+',new[]{"Ctrl","Alt","Shift","Win"}.Where(modifiers.Contains).Select(m=>m switch { "Ctrl"=>"CTRL","Alt"=>"ALT","Shift"=>"SHIFT",_=>"LOGO" }).Append(symbol));
        }
    }
}
internal sealed record PortalShortcut(string Id,string Description,string PreferredTrigger);
internal sealed record BoundShortcut(string Id,string Description,string TriggerDescription);
internal static class GlobalShortcutSettings
{
    internal static readonly string[] Ids=["fullscreen","windowed","toolbar"];
    internal static GlobalShortcutBinding Binding(RuntimeSettings settings,string id)=>id switch { "fullscreen"=>settings.FullscreenShortcut,"windowed"=>settings.WindowedShortcut,"toolbar"=>settings.ToolbarShortcut,_=>throw new InvalidDataException("Unknown shortcut action.") };
    internal static void Validate(RuntimeSettings settings)
    {
        string[] triggers=Ids.Select(id=>(Binding(settings,id)??throw new InvalidDataException("Missing shortcut binding.")).Trigger).ToArray();
        if(triggers.Distinct(StringComparer.Ordinal).Count()!=triggers.Length)throw new InvalidDataException("Each action needs a different shortcut.");
    }
    internal static PortalShortcut[] Definitions(RuntimeSettings settings)=>Ids.Select(id=>new PortalShortcut(id,Localization.Get(id switch { "fullscreen"=>"Home_Activation_FullscreenScaling_ShortcutControl.Title","windowed"=>"Home_Activation_WindowedScaling_ShortcutControl.Title",_=>"Home_Toolbar_StateToggle_ShortcutControl.Title" }),Binding(settings,id).Trigger)).ToArray();
}
