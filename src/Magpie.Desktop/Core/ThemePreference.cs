using Avalonia;
using Avalonia.Styling;
namespace Magpie.Desktop.Core;

internal static class ThemePreference
{
    // Original SettingsViewModel order: System, Light, Dark.
    internal static ThemeVariant Variant(int choice)=>choice switch
    {
        0=>ThemeVariant.Default,1=>ThemeVariant.Light,2=>ThemeVariant.Dark,
        _=>throw new InvalidDataException("Invalid application theme.")
    };
    internal static void Apply(Application application,int choice)=>application.RequestedThemeVariant=Variant(choice);
}
