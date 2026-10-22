using System.Text.Json;
using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;

internal sealed record LocalizationProbeResult(string Language,string Home,string Defaults,string SystemDefault);
[JsonSerializable(typeof(LocalizationProbeResult))]
internal partial class LocalizationProbeJson : JsonSerializerContext { }
internal static class LocalizationProbe
{
    // A child-process startup check. It never initializes Avalonia or opens a window.
    internal static int Run(string path)
    {
        try
        {
            var settings=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings) ?? throw new InvalidDataException("Empty probe settings.");
            settings.Validate();Localization.Configure(settings.Language);
            var result=new LocalizationProbeResult(Localization.CurrentLanguage,Localization.Get("Root_Home.Content"),Localization.Get("Root_Defaults.Content"),Localization.Get("Settings_General_Language_System"));
            Console.WriteLine(JsonSerializer.Serialize(result,LocalizationProbeJson.Default.LocalizationProbeResult));return 0;
        }
        catch(Exception failure) { Console.Error.WriteLine(failure.Message);return 1; }
    }
}
