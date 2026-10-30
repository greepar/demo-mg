using System.Text.Json;
namespace Magpie.Desktop.Core;

internal static class OriginalConfigurationTests
{
    internal const string Example="""
    {
      "version":5,"language":"zh-Hans","theme":2,"countdownSeconds":5,"showNotifyIcon":false,
      "developerMode":true,"keepScreenOn":true,"disableFP16":true,"disableTopmost":true,"duplicateFrameDetectionMode":2,"minFrameRate":12,
      "enableStatisticsForDynamicDetection":true,"futureSetting":{"nested":[1,"keep"]},
      "shortcuts":{"scale":3394,"windowedModeScale":3153,"toolbar":3140,"takeScreenshot":3155},
      "scalingModes":[
        {"name":"Detail","effects":[{"name":"Bicubic","scalingType":0,"scale":{"x":2,"y":3},"parameters":{"paramB":0.2,"paramC":0.7}}]},
        {"name":"Deferred","effects":[{"name":"Custom\\NotInstalled","parameters":{"strength":0.7}}]}
      ],
      "profiles":[
        {"scalingMode":1,"captureMethod":2,"captureTitleBar":true,"initialWindowedScaleFactor":6,"customInitialWindowedScaleFactor":2.25,
         "fullscreenInitialToolbarState":0,"windowedInitialToolbarState":1,"screenshotsDir":"C:\\Pictures\\Magpie",
         "screenshotFilenameTemplate":"%WT%_%PN%_%MS%","frameRateLimiterEnabled":true,"maxFrameRate":144,"autoHideCursorEnabled":true,"autoHideCursorDelay":0.7,"adjustCursorSpeed":false,
         "croppingEnabled":true,"cropping":{"left":1.5,"top":2,"right":3,"bottom":4},"outputAlignment":8,
         "graphicsCardId":{"idx":1,"vendorId":4098,"deviceId":123},"3DGameMode":true},
        {"name":"游戏测试","packaged":false,"pathRule":"C:\\Games\\example.exe","classNameRule":"Game*",
         "launcherPath":"..\\start.lnk","launchParameters":"--name \"two words\"","scalingMode":0,"captureMethod":0,
         "captureTitleBar":false,"autoScale":2,"cursorScaling":7,"customCursorScaling":1.7},
        {"name":"Packaged app","packaged":true,"pathRule":"Package!App","classNameRule":"ApplicationFrameWindow",
         "scalingMode":-1,"captureMethod":1}
      ],"overlay":{"windows":{"stats":{"hArea":1,"vArea":2,"hPos":0.3,"vPos":0.7}}}
    }
    """;
    private static void Require(bool value,string message)
    { if(!value)throw new InvalidOperationException("Original configuration migration: "+message); }
    private static void Reject(string value)
    {
        try { _=OriginalConfiguration.Parse(value); }
        catch(Exception error) when(error is InvalidDataException or JsonException) { return; }
        throw new InvalidOperationException("Malformed original configuration was accepted.");
    }
    internal static void Verify()
    {
        var current=RuntimeSettings.Current;long revision=RuntimeSettings.ChangeSequence;
        string currentJson=JsonSerializer.Serialize(current,SettingsJson.Default.RuntimeSettings);
        var result=OriginalConfiguration.Parse(Example);var root=result.Settings;
        Require(ReferenceEquals(current,RuntimeSettings.Current)&&RuntimeSettings.ChangeSequence==revision&&currentJson==JsonSerializer.Serialize(current,SettingsJson.Default.RuntimeSettings),"Parsing modified live preferences.");
        Require(root.Profiles.Count==2&&root.Modes.Count==3&&root.SelectedModeId==root.Modes[1].Id,"Default or mode-index references were lost.");
        var application=root.Profiles[0];
        Require(!root.AdjustCursorSpeed&&application.Settings.AdjustCursorSpeed,"Original pointer speed preference was lost or inherited across profiles.");
        Require(application.Settings.CursorScaling==7&&application.Settings.CustomCursorScaleFactor==1.7f&&root.CursorScaling==2&&root.CursorInterpolation==0,"Original cursor scale choice or custom factor was lost or inherited.");
        Require(root.AutoHideCursorEnabled&&root.AutoHideCursorDelay==.7f&&!application.Settings.AutoHideCursorEnabled&&application.Settings.AutoHideCursorDelay==3,"Cursor idle preferences were lost or inherited across profiles.");
        Require(application.Settings.SelectedModeId==root.Modes[0].Id&&root.Profiles[1].Settings.SelectedModeId==root.Modes[2].Id,"Application index references were renumbered.");
        Require(root.Modes[0].Steps[0].Effect==3&&root.Modes[0].Steps[0].Parameter1==.2f&&root.Modes[0].Steps[0].Parameter2==.7f,"Effect parameters were lost.");
        Require(!root.Modes[1].IsAvailable&&root.Modes[1].Steps[0].DeferredEffect.HasValue&&!root.Modes[2].IsAvailable,"Unsupported effects or no-selection became runnable.");
        try { root.Profiles[1].Settings.EnsureSelectedModeAvailable();throw new InvalidOperationException("Unselected profile passed preflight."); }
        catch(InvalidOperationException error) when(error.Message.Contains("no effects")) { }
        Require(root.DeveloperMode&&root.Language=="zh-Hans"&&root.Theme==2&&root.CountdownSeconds==5&&!root.ShowTrayIcon&&root.KeepScreenOn&&root.DisableFP16&&root.DisableTopmost&&root.DuplicateFrameDetection==DuplicateFrameMode.Never&&root.MinimumFrameRate==12&&root.StatisticsForDynamicDetection,"Global preferences were not mapped.");
        Require(root.FullscreenShortcut.Key=="B"&&root.FullscreenShortcut.Modifiers=="Alt,Shift,Win"&&root.WindowedShortcut.Key=="Q"&&root.ToolbarShortcut.Key=="D"&&root.ScreenshotShortcut.Key=="S","Original VK/modifier encoding was decoded incorrectly.");
        Require(root.FullscreenInitialToolbarState==ToolbarState.Off&&root.WindowedInitialToolbarState==ToolbarState.AlwaysShow&&root.InitialWindowedScaleFactor==6&&root.CustomInitialWindowedScaleFactor==2.25f&&root.OutputAlignment==8&&root.IsCroppingEnabled&&root.CroppingLeft==1.5f&&root.CroppingBottom==4&&root.FrameRateLimiterEnabled&&root.MaximumFrameRate==144,"Default profile options were lost.");
        Require(!application.Settings.IsCroppingEnabled&&application.Settings.CroppingLeft==0&&application.Settings.InitialWindowedScaleFactor==0&&application.Settings.FullscreenInitialToolbarState==ToolbarState.AutoHide&&application.Settings.MaximumFrameRate==60,"Application options incorrectly inherited the default profile.");
        Require(root.CaptureBackend==CaptureBackend.Gdi&&application.Settings.CaptureBackend==CaptureBackend.GraphicsCapture&&root.Profiles[1].Settings.CaptureBackend==CaptureBackend.DesktopDuplication,"Windows capture choices were silently replaced.");
        Require(!application.CanLaunch&&application.ExecutablePath==""&&application.Arguments.Count==0&&!application.MatchesExecutable(Path.Combine(Path.GetTempPath(),"example.exe")),"Foreign identity became a fake local executable.");
        try { application.CreateLaunchInfo();throw new InvalidOperationException("Imported foreign identity was launchable."); }
        catch(InvalidOperationException error) when(error.Message.Contains("local executable")) { }
        Require(application.OriginalProfile!.Value.GetProperty("launchParameters").GetString()=="--name \"two words\"","Windows argument quoting was altered.");
        if(!OperatingSystem.IsWindows())Require(root.ScreenshotDirectory=="","Foreign screenshot path became a local path.");
        Require(root.ScreenshotTemplate=="%WT%_%PN%_%MS%"&&root.OriginalConfiguration!.Value.GetProperty("futureSetting").GetProperty("nested")[1].GetString()=="keep","Original archive or screenshot format was lost.");
        foreach(string field in new[]{"futureSetting","graphicsCardId","3DGameMode","autoScale","overlay.windows"})
            Require(result.Warnings.Any(w=>w.Contains(field,StringComparison.Ordinal)),"Missing migration review warning: "+field);
        Require(application.Settings.DisableTopmost,"Global topmost preference was lost in imported application settings.");
        Require(!OriginalConfiguration.Parse(Example.Replace("\"disableTopmost\":true,","")).Settings.DisableTopmost,"Missing topmost preference did not retain original automatic policy.");
        Reject(Example.Replace("\"disableTopmost\":true","\"disableTopmost\":1"));
        var cloned=root.CopyForProfile();Require(!cloned.OriginalConfiguration.HasValue,"Profile copies duplicated the complete root archive.");
        Require(root.CaptureTitleBar&&!application.Settings.CaptureTitleBar&&cloned.CaptureTitleBar,"Original per-profile caption choices were lost or inherited from defaults.");
        Require(OriginalConfiguration.Parse(Example.Replace("captureTitleBar","reserveTitleBar")).Settings.CaptureTitleBar,"Legacy title bar flag was not migrated.");
        Require(OriginalConfiguration.Parse("\uFEFF/*comment*/"+Example.Replace("\"version\":5,","\"version\":5,/* legacy comment */")).Settings.Profiles.Count==2,"Original BOM/comments were rejected.");
        var legacy=OriginalConfiguration.Parse(Example.Replace("\"profiles\"","\"scalingProfiles\"").Replace("\"shortcuts\"","\"hotkeys\"").Replace("\"toolbar\":3140","\"overlay\":3140").Replace("\"countdownSeconds\"","\"downCount\"").Replace("\"showNotifyIcon\"","\"showTrayIcon\"").Replace("\"overlay\":{\"windows\"","\"overlay\":{\"initialToolbarState\":1,\"screenshotsDir\":\"\",\"windows\""));
        Require(legacy.Settings.CountdownSeconds==5&&!legacy.Settings.ShowTrayIcon&&legacy.Settings.ToolbarShortcut.Key=="D"&&legacy.Settings.FullscreenInitialToolbarState==ToolbarState.AlwaysShow&&legacy.Settings.Profiles.All(p=>p.Settings.FullscreenInitialToolbarState==ToolbarState.AlwaysShow&&p.Settings.WindowedInitialToolbarState==ToolbarState.AlwaysShow&&p.Settings.ScreenshotDirectory==""),"Legacy global overlay or property aliases were not migrated.");
        foreach(string malformed in new[]{"{}","[]","null","{\"scalingModes\":[]}",Example.Replace("\"version\":5","\"version\":5,\"version\":5"),Example.Replace("\"theme\":2","\"theme\":\"dark\""),Example.Replace("\"maxFrameRate\":144","\"maxFrameRate\":1e999"),Example.Replace("\"pathRule\":\"Package!App\"","\"pathRule\":\"\""),Example.Replace("\"name\":\"Packaged app\"","\"name\":\"游戏测试\""),Example.Replace("\"croppingEnabled\":true","\"croppingEnabled\":0"),Example.Replace("\"maxFrameRate\":144","\"maxFrameRate\":-1")})Reject(malformed);
        var unsupported=OriginalConfiguration.Parse(Example.Replace("\"scale\":3394","\"scale\":0").Replace("\"language\":\"zh-Hans\"","\"language\":\"unsupported\""));
        Require(unsupported.Settings.Language==""&&unsupported.Settings.FullscreenShortcut.Key=="A"&&unsupported.Warnings.Any(w=>w.Contains("disabled or unsupported")),"Deferred shortcut/language preferences were not reported.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-original-config-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string source=Path.Combine(directory,"原配置 source.json"),destination=Path.Combine(directory,"desktop converted.json");
            File.WriteAllText(source,Example);
            Require(OriginalConfiguration.RunAsync(source,destination).GetAwaiter().GetResult()==0,"Offline migration command failed.");
            var restored=RuntimeSettings.ReadFile(destination);restored.Validate();
            Require(restored.DisableTopmost&&restored.Profiles[0].Settings.DisableTopmost,"Topmost preference did not survive AOT migration persistence.");
            Require(JsonElement.DeepEquals(restored.OriginalConfiguration!.Value,root.OriginalConfiguration!.Value)&&JsonElement.DeepEquals(restored.Profiles[0].OriginalProfile!.Value,application.OriginalProfile!.Value)&&restored.Profiles[0].Settings.SelectedModeId==restored.Modes[0].Id,"NativeAOT persistence lost original data or references.");
            string saved=File.ReadAllText(destination);
            string active=Path.Combine(directory,"active.json");File.WriteAllText(active,"exact previous settings bytes\n");
            try { ConfigurationImport.Install(root,active,()=>throw new IOException("restart unavailable"));throw new InvalidOperationException("Restart failure was accepted."); }
            catch(IOException failure) when(failure.Message.Contains("original configuration was restored")) { }
            Require(File.ReadAllText(active)=="exact previous settings bytes\n"&&Directory.GetFiles(directory,"*.before-import-*.json").Single() is {} backup&&File.ReadAllText(backup)=="exact previous settings bytes\n","Restart failure did not restore/backup exact original bytes.");
            string fresh=Path.Combine(directory,"fresh.json");
            try { ConfigurationImport.Install(root,fresh,()=>throw new IOException("restart unavailable"));throw new InvalidOperationException("First-install restart failure was accepted."); }
            catch(IOException failure) when(failure.Message.Contains("original configuration was restored")) { }
            Require(!File.Exists(fresh),"Failed first import left an installed configuration.");
            int starts=0;string? savedBackup=ConfigurationImport.Install(root,active,()=>starts++);
            Require(starts==1&&savedBackup is not null&&File.ReadAllText(savedBackup)=="exact previous settings bytes\n"&&RuntimeSettings.ReadFile(active).Profiles.Count==2,"Successful installation lost backups or settings.");
            string concurrent=Path.Combine(directory,"concurrent.json");File.WriteAllText(concurrent,"original");
            try { ConfigurationImport.Install(root,concurrent,()=> { File.WriteAllText(concurrent,"another writer");throw new IOException("restart refused"); });throw new InvalidOperationException("Concurrent-write rollback was accepted."); }
            catch(IOException failure) when(failure.Message.Contains("could not be restored")) { }
            Require(File.ReadAllText(concurrent)=="another writer","Rollback overwrote a concurrent configuration writer.");
            Require(OriginalConfiguration.RunAsync(source,destination).GetAwaiter().GetResult()==1&&saved==File.ReadAllText(destination)&&File.ReadAllText(source)==Example,"Migration overwrote source or an existing output.");
            Require(OriginalConfiguration.RunAsync(source,source).GetAwaiter().GetResult()==1&&File.ReadAllText(source)==Example,"Migration overwrote its source.");
            string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"keep");
            Require(OriginalConfiguration.RunAsync(source,Path.Combine(blocked,"output.json")).GetAwaiter().GetResult()==1&&File.ReadAllText(blocked)=="keep","Failed output damaged existing files.");
            Require(Directory.GetFiles(directory,"*.tmp").Length==0,"Temporary files were left after a failed migration.");
        }
        finally { Directory.Delete(directory,true); }
        Require(ReferenceEquals(current,RuntimeSettings.Current)&&revision==RuntimeSettings.ChangeSequence&&currentJson==JsonSerializer.Serialize(current,SettingsJson.Default.RuntimeSettings),"Offline migration changed live configuration.");
        Console.WriteLine("PASS original-config: global/default/app migration, mode references, deferred data, legacy aliases, AOT persistence and no-overwrite failures");
    }
}
