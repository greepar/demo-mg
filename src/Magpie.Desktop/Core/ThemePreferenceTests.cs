using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class ThemePreferenceTests
{
    internal static void Verify()
    {
        string folder=Path.Combine(Path.GetTempPath(),"magpie-theme-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(folder,"settings.json");
            foreach(int theme in new[]{0,1,2})
            {
                var settings=new RuntimeSettings { Theme=theme };settings.WriteFile(path);
                if(RuntimeSettings.ReadFile(path).Theme!=theme)throw new InvalidOperationException("Theme preference disk restore failed.");
                var command=ApplicationRestart.CurrentStartInfo(["--localization-probe",path]);
                command.RedirectStandardOutput=true;command.RedirectStandardError=true;
                using var child=System.Diagnostics.Process.Start(command)!;
                var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
                if(!child.WaitForExit(30000)) { child.Kill();throw new InvalidOperationException("Theme restart test timed out."); }
                if(child.ExitCode!=0)throw new InvalidOperationException(error.GetAwaiter().GetResult());
                var result=JsonSerializer.Deserialize(output.GetAwaiter().GetResult(),LocalizationProbeJson.Default.LocalizationProbeResult)!;
                if(result.Theme!=theme || result.ThemeVariant!=ThemePreference.Variant(theme).Key.ToString())throw new InvalidOperationException("Replacement process did not restore saved theme.");
                settings.Theme=3;
                try { settings.WriteFile(path);throw new Exception("Invalid theme saved."); }catch(InvalidDataException) { }
                if(RuntimeSettings.ReadFile(path).Theme!=theme)throw new InvalidOperationException("Invalid theme damaged persisted preference.");
            }
            var legacy=JsonSerializer.Deserialize("{}",SettingsJson.Default.RuntimeSettings)!;
            if(legacy.Theme!=0)throw new InvalidOperationException("Legacy settings must follow the system theme.");
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        Console.WriteLine("THEME PASS: System/Light/Dark, AOT disk roundtrip, replacement process preference and variant restoration, invalid save preservation and legacy default.");
    }
}
