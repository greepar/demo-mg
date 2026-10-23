using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class CaptureBackendTests
{
    internal static void Verify()
    {
        var expected=OperatingSystem.IsWindows()?CaptureBackend.Gdi:OperatingSystem.IsMacOS()?CaptureBackend.ScreenCaptureKit:CaptureBackend.Portal;
        if(CaptureBackendPolicy.Available!=expected)throw new InvalidOperationException("Capture backend ABI does not match the platform implementation.");
        foreach(var available in new[]{CaptureBackend.Portal,CaptureBackend.ScreenCaptureKit,CaptureBackend.Gdi})
        foreach(var requested in Enum.GetValues<CaptureBackend>())
        {
            bool supported=requested==CaptureBackend.Native||requested==available;
            try
            {
                var actual=CaptureBackendPolicy.Resolve(requested,available);
                if(!supported||actual!=available)throw new InvalidOperationException("Capture selection silently substituted a different backend.");
            }
            catch(NotSupportedException) { if(supported)throw; }
        }
        string directory=Path.Combine(Path.GetTempPath(),"magpie-capture-policy-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(directory,"settings.json");
            foreach(var backend in Enum.GetValues<CaptureBackend>())
            {
                var settings=new RuntimeSettings { CaptureBackend=backend };
                var profile=settings.AddProfile("Capture fixture",Path.GetFullPath(Path.Combine(directory,"source")));
                settings.WriteFile(path);var restored=RuntimeSettings.ReadFile(path);
                if(restored.CaptureBackend!=backend||restored.Profiles.Single().Settings.CaptureBackend!=backend)throw new InvalidOperationException("Capture selection lost during profile copy or AOT persistence.");
                profile.Settings.CaptureBackend=CaptureBackend.Native;
                if(settings.CaptureBackend!=backend)throw new InvalidOperationException("Application capture preference modified defaults.");
                foreach(var invalid in new[]{(CaptureBackend)(-1),(CaptureBackend)99})
                {
                    settings.CaptureBackend=invalid;
                    try { settings.WriteFile(path);throw new InvalidOperationException("Invalid capture selection saved."); }catch(InvalidDataException) { }
                    if(RuntimeSettings.ReadFile(path).CaptureBackend!=backend)throw new InvalidOperationException("Invalid capture selection damaged saved settings.");
                }
            }
            if(JsonSerializer.Deserialize("{}",SettingsJson.Default.RuntimeSettings)!.CaptureBackend!=CaptureBackend.Native)throw new InvalidOperationException("Legacy configuration must select the native backend.");
            foreach(var invalid in new[]{(CaptureBackend)(-1),(CaptureBackend)99})
                try { CaptureBackendPolicy.Resolve(invalid,expected);throw new InvalidOperationException("Invalid backend accepted."); }catch(InvalidDataException) { }
            try { CaptureBackendPolicy.Resolve(CaptureBackend.Native,CaptureBackend.Native);throw new InvalidOperationException("Unknown native backend accepted."); }catch(InvalidDataException) { }
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("CAPTURE BACKEND PASS: real native ABI, three-platform selection matrix, no silent fallback, AOT disk/profile preservation, invalid-save protection and legacy default.");
    }
}
