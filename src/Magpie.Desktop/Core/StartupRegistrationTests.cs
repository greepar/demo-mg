using System.Diagnostics;
using System.Xml.Linq;
namespace Magpie.Desktop.Core;

internal static class StartupRegistrationTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"magpie-startup-"+Guid.NewGuid().ToString("N")+" space ");Directory.CreateDirectory(root);
        try
        {
            string user=Path.Combine(root,"user","magpie.desktop"),system=Path.Combine(root,"system.desktop");
            string[] arguments=["/opt/Magpie a%$`\\\"/Magpie","", "a b", "100%", "$HOME", "`echo bad`", "a\\b", "a\"b"];
            string entry=StartupRegistration.DesktopEntry(arguments,root),hidden=StartupRegistration.DesktopEntry(arguments,root,true);
            var registration=new FileStartupRegistration(user,entry,hidden,[system],StartupRegistration.Marker);
            Check(!registration.Enabled,"Initially disabled");registration.SetEnabled(true);Check(registration.Enabled,"Enable");
            Check(File.ReadAllText(user)==entry,"Exact command retained");registration.SetEnabled(false);Check(!File.Exists(user)&&!registration.Enabled,"Remove user entry");
            File.WriteAllText(system,entry);Check(registration.Enabled,"Inherited system entry");registration.SetEnabled(false);
            Check(File.ReadAllText(user)==hidden&&!registration.Enabled&&File.ReadAllText(system)==entry,"Mask system entry without editing it");
            File.WriteAllText(user,hidden.Replace("/opt/Magpie", "/old/Magpie"));Check(!registration.Enabled,"Old installation mask still disables startup");
            registration.SetEnabled(true);Check(registration.Enabled,"Re-enable masked entry");
            string upgraded=StartupRegistration.DesktopEntry([..arguments,"--autostart"],root);
            var migration=new FileStartupRegistration(user,upgraded,hidden,[],StartupRegistration.Marker,entry);
            Check(migration.Enabled,"Previous launch command remains recognized without rewriting it");
            Check(File.ReadAllText(user)==entry,"Reading legacy startup does not modify it");
            migration.SetEnabled(true);Check(File.ReadAllText(user)==upgraded&&migration.Enabled,"Explicit enable upgrades startup command");
            File.WriteAllText(user,entry);
            var traySettings=new RuntimeSettings { ShowTrayIcon=false,CountdownSeconds=5 };
            string settingsPath=Path.Combine(root,"settings.json");traySettings.WriteFile(settingsPath);
            var restored=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(settingsPath),SettingsJson.Default.RuntimeSettings)!;
            restored.Validate();Check(!restored.ShowTrayIcon&&restored.CountdownSeconds==5,"Tray/countdown disk persistence");
            var defaults=System.Text.Json.JsonSerializer.Deserialize("{}",SettingsJson.Default.RuntimeSettings)!;
            Check(defaults.ShowTrayIcon&&defaults.CountdownSeconds==3,"Legacy settings get original tray/countdown defaults");
            foreach(int invalid in new[]{0,6})
            {
                restored.CountdownSeconds=invalid;
                try { restored.Validate();throw new InvalidOperationException("Invalid countdown accepted"); }catch(InvalidDataException) { }
            }
            File.WriteAllText(user,"[Desktop Entry]\nName=Other\n");
            try { registration.SetEnabled(true);throw new InvalidOperationException("Overwrote unrelated entry"); }catch(IOException) { }
            Check(File.ReadAllText(user).Contains("Name=Other"),"Collision preserved");
            File.Delete(user);File.Delete(system);Directory.Delete(Path.GetDirectoryName(user)!);File.WriteAllText(Path.GetDirectoryName(user)!,"blocked");
            try { registration.SetEnabled(true);throw new InvalidOperationException("Accepted inaccessible destination"); }catch(IOException) { }
            Check(!registration.Enabled,"Failed write stays disabled");
            Check(StartupRegistration.WindowsArgument("a b\\")=="\"a b\\\\\"","Windows trailing slash");
            Check(StartupRegistration.WindowsArgument("a\"b")=="\"a\\\"b\"","Windows embedded quote");
            foreach(string invalid in new[]{"a\nb","a\0b","a\rb"})
                try { StartupRegistration.DesktopArgument(invalid);throw new InvalidOperationException("Accepted control character"); }catch(InvalidDataException) { }
            var plist=XDocument.Parse(StartupRegistration.LaunchAgent(arguments,root));
            Check(plist.Descendants("array").Single().Elements("string").Select(e=>e.Value).SequenceEqual(arguments),"LaunchAgent arguments");
            Check(plist.Descendants("true").Count()==1&&!plist.Descendants("key").Any(e=>e.Value=="KeepAlive"),"LaunchAgent login-only behavior");
            if(OperatingSystem.IsLinux()) { VerifyGio(root,true);VerifyGio(root,false); }
            Console.WriteLine("PASS: startup temporary-file lifecycle, system mask, collision/write failure, serializers"+(OperatingSystem.IsLinux() ? " and headless GIO argv roundtrip." : ". GIO probe is Linux-only."));
        }
        finally { Directory.Delete(root,true); }
    }
    private static void VerifyGio(string root,bool percent)
    {
        // GIO is an independent Desktop Entry parser. The child never initializes Avalonia.
        string receipt=Path.Combine(root,percent ? "receipt-percent.txt" : "receipt-normal.txt");
        var current=ApplicationRestart.CurrentStartInfo([]);
        string alias=Path.Combine(root,percent ? "Magpie % $ ` \\\" executable " : "Magpie $ ` \\\" executable ");File.CreateSymbolicLink(alias,current.FileName);
        string[] expected=["", "two words", "100%", "$HOME", "`echo bad`", "a\\b", "a\"b", "&;()<>*?~#'|"];
        string[] command=[alias,..current.ArgumentList,"--startup-command-probe",receipt,..expected];
        string desktop=Path.Combine(root,"probe.desktop");File.WriteAllText(desktop,StartupRegistration.DesktopEntry(command,root));
        var info=new ProcessStartInfo("gio") { UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true };
        info.ArgumentList.Add("launch");info.ArgumentList.Add(desktop);
        foreach(string key in new[]{"DISPLAY","WAYLAND_DISPLAY","WAYLAND_SOCKET","DBUS_SESSION_BUS_ADDRESS"})info.Environment.Remove(key);
        using var process=Process.Start(info)!;
        if(!process.WaitForExit(10000)) { process.Kill(true);throw new InvalidOperationException("GIO startup probe timed out."); }
        if(process.ExitCode!=0)throw new InvalidOperationException("GIO startup probe: "+process.StandardError.ReadToEnd());
        if(!SpinWait.SpinUntil(()=>File.Exists(receipt+".ready"),TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Startup child receipt missing.");
        Check(File.ReadAllLines(receipt).SequenceEqual(expected.Append(root)),"Real GIO argument and working directory roundtrip");
    }
    private static void Check(bool condition,string name) { if(!condition)throw new InvalidOperationException("Startup test failed: "+name); }
}
