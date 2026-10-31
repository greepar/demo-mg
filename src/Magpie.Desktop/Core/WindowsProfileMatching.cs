using System.Text.Json;
namespace Magpie.Desktop.Core;

internal enum AutoScaleMode { Disabled, Fullscreen, Windowed }

// Windows identities are deliberately separate from local launch paths. An
// imported executable/AUMID must never turn into a Unix executable identity.
internal sealed record WindowsWindowIdentity(bool Packaged,string PathOrAumid,string ClassName);

internal static class WindowsProfileMatching
{
    internal static string NormalizeClass(string value)
    {
        const string wpf="HwndWrapper[";
        if(value.StartsWith(wpf,StringComparison.Ordinal)&&value.EndsWith(']'))
        {
            string inner=value[wpf.Length..^1];int separator=inner.LastIndexOf(";;",StringComparison.Ordinal);
            if(separator>=0&&!inner.AsSpan(0,separator).ContainsAny("\r\n\u2028\u2029")&&OriginalWpfGuid(inner.AsSpan(separator+2)))return inner[..separator];
        }
        if(DigitsAfter(value,"Chrome_WidgetWin_"))return "Chrome_WidgetWin_1";
        if(DigitsAfter(value,"XWindow_"))return "XWindow_0";
        return value;
    }
    private static bool DigitsAfter(string value,string prefix)
    {
        if(!value.StartsWith(prefix,StringComparison.Ordinal))return false;
        // Original loops over the suffix; an empty suffix is accepted too.
        foreach(char c in value.AsSpan(prefix.Length))if(c is <'0' or >'9')return false;
        return true;
    }
    private static bool OriginalWpfGuid(ReadOnlySpan<char> suffix)
    {
        if(suffix.Length!=36)return false;
        for(int i=0;i<suffix.Length;i++)
        {
            if(i is 8 or 13 or 18 or 23) {if(suffix[i]!='-')return false;}
            // Match ProfileService's actual [0-9,a-f] expression, including
            // its comma and lowercase-only behavior; Guid.TryParse differs.
            else if(suffix[i] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not ',')return false;
        }
        return true;
    }
    internal static ApplicationProfile? Find(IEnumerable<ApplicationProfile> profiles,WindowsWindowIdentity source,bool automatic)
    {
        string windowClass=NormalizeClass(source.ClassName);
        foreach(var profile in profiles)
        {
            if(automatic&&profile.Settings.AutoScale==AutoScaleMode.Disabled)continue;
            if(profile.OriginalProfile is not {ValueKind:JsonValueKind.Object} rule)continue;
            if(!rule.TryGetProperty("packaged",out var packaged)||packaged.ValueKind is not (JsonValueKind.True or JsonValueKind.False)||packaged.GetBoolean()!=source.Packaged)continue;
            if(!rule.TryGetProperty("classNameRule",out var className)||className.ValueKind!=JsonValueKind.String||className.GetString()!=windowClass)continue;
            if(rule.TryGetProperty("pathRule",out var path)&&path.ValueKind==JsonValueKind.String&&path.GetString()==source.PathOrAumid)return profile;
        }
        return null;
    }
    internal static RuntimeSettings ResolveManual(RuntimeSettings root,WindowsWindowIdentity? source)
        =>source is not null&&Find(root.Profiles,source,false) is {} profile?profile.Settings:root;
    internal static AutoScaleMode ReadAutoScale(JsonElement source)
    {
        if(!source.TryGetProperty("autoScale",out var value))return AutoScaleMode.Disabled;
        return value.ValueKind switch
        {
            JsonValueKind.True=>AutoScaleMode.Fullscreen,
            JsonValueKind.Number when value.TryGetUInt32(out uint number)&&number<3=>(AutoScaleMode)number,
            _=>AutoScaleMode.Disabled
        };
    }
}
