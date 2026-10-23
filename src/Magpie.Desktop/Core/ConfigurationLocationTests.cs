using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class ConfigurationLocationTests
{
    internal static void Verify()
    {
        string root=Path.Combine(Path.GetTempPath(),"magpie-config-"+Guid.NewGuid().ToString("N"));
        string user=Path.Combine(root,"user preferences",ConfigurationLocation.FileName),portable=Path.Combine(root,"application with spaces","config",ConfigurationLocation.FileName);
        try
        {
            var settings=new RuntimeSettings { Theme=2,Language="zh-Hans",WindowedInitialToolbarState=ToolbarState.Off };
            var location=new ConfigurationLocation(user,portable);
            if(location.Portable||location.Path!=user)throw new InvalidOperationException("Fresh startup selected portable mode.");
            settings.WriteFile(user);location.SetPortable(settings,true);
            if(!location.Portable||location.Path!=portable||!File.Exists(user))throw new InvalidOperationException("Enabling portable mode lost the user settings backup.");
            VerifyRestart(user,portable,true,2);
            settings.Theme=1;settings.WriteFile(location.Path);
            if(RuntimeSettings.ReadFile(user).Theme!=2)throw new InvalidOperationException("Portable save changed user preferences.");
            string moved=Path.Combine(root,"moved application","config",ConfigurationLocation.FileName);Directory.CreateDirectory(Path.GetDirectoryName(moved)!);File.Copy(portable,moved);
            VerifyRestart(user,moved,true,1);
            location.SetPortable(settings,false);
            if(location.Portable||File.Exists(portable)||RuntimeSettings.ReadFile(user).Theme!=1)throw new InvalidOperationException("Disabling portable mode did not preserve current preferences.");
            VerifyRestart(user,portable,false,1);
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"file instead of directory");
            var cannotWrite=new ConfigurationLocation(user,Path.Combine(blocked,ConfigurationLocation.FileName));
            try { cannotWrite.SetPortable(settings,true);throw new Exception("Unwritable portable location accepted."); }catch(IOException) { }
            if(cannotWrite.Portable||RuntimeSettings.ReadFile(user).Theme!=1)throw new InvalidOperationException("Failed enable changed active preferences.");
            location.SetPortable(settings,true);
            var cannotDelete=new ConfigurationLocation(user,portable,_=>throw new IOException("fixture delete failure"));
            settings.Theme=2;
            try { cannotDelete.SetPortable(settings,false);throw new Exception("Failed portable deletion accepted."); }catch(IOException) { }
            if(!cannotDelete.Portable || !File.Exists(portable) || RuntimeSettings.ReadFile(portable).Theme!=1)throw new InvalidOperationException("Failed disable lost portable ownership.");
            string blockedUser=Path.Combine(blocked,"user.json");var userFailure=new ConfigurationLocation(blockedUser,portable);
            try { userFailure.SetPortable(settings,false);throw new Exception("Unwritable user location accepted."); }catch(IOException) { }
            if(!userFailure.Portable||!File.Exists(portable))throw new InvalidOperationException("User write failure removed portable settings.");
            if(ConfigurationLocation.ApplicationDirectory(Path.Combine(root,"Magpie.app","Contents","MacOS"),true)!=root)throw new InvalidOperationException("macOS portable data would modify the signed app bundle.");
            if(ConfigurationLocation.ApplicationDirectory(Path.Combine(root,"app"),false)!=Path.Combine(root,"app"))throw new InvalidOperationException("Non-bundle application directory was changed.");
            string literal=Path.Combine(root,"folder with spaces","$literal & quote'",ConfigurationLocation.FileName);
            var mac=ConfigurationReveal.NativeCommand(literal,false);var windows=ConfigurationReveal.NativeCommand(literal,true);
            if(mac.UseShellExecute||mac.ArgumentList.Count!=2||mac.ArgumentList[0]!="-R"||mac.ArgumentList[1]!=literal||windows.UseShellExecute||windows.ArgumentList.Count!=1||windows.ArgumentList[0]!="/select,"+literal)throw new InvalidOperationException("Native file selection command did not preserve literal arguments.");
            if(Directory.GetFiles(root,"*.tmp",SearchOption.AllDirectories).Length!=0)throw new InvalidOperationException("Configuration migration leaked temporary files.");
        }
        finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        Console.WriteLine("CONFIGURATION PASS: portable/user selection, transactional migration, original backup, moved install, fresh-process restore, write/delete failure ownership, app-bundle placement and temporary cleanup.");
    }
    private static void VerifyRestart(string user,string portable,bool expectedPortable,int expectedTheme)
    {
        var command=ApplicationRestart.CurrentStartInfo(["--configuration-probe",user,portable]);command.RedirectStandardOutput=true;command.RedirectStandardError=true;
        using var child=System.Diagnostics.Process.Start(command)!;
        var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
        if(!child.WaitForExit(30000)) { child.Kill();throw new InvalidOperationException("Configuration restart test timed out."); }
        string expected=(expectedPortable?"portable":"user")+":"+expectedTheme;
        if(child.ExitCode!=0 || output.GetAwaiter().GetResult().Trim()!=expected)throw new InvalidOperationException("Replacement process lost configuration mode: "+error.GetAwaiter().GetResult());
    }
    internal static int Probe(string user,string portable)
    {
        var location=new ConfigurationLocation(user,portable);var settings=RuntimeSettings.ReadFile(location.Path);
        Console.WriteLine((location.Portable?"portable":"user")+":"+settings.Theme);return 0;
    }
}
