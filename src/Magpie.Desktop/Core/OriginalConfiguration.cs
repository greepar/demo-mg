using System.Text.Json;
namespace Magpie.Desktop.Core;

internal sealed record OriginalConfigurationResult(RuntimeSettings Settings,IReadOnlyList<string> Warnings);

// Migration is deliberately offline: parsing never changes Current, registers shortcuts or launches a process.
// The complete source document is retained so later ports can recover settings they do not yet implement.
internal static class OriginalConfiguration
{
    private static readonly HashSet<string> GlobalFields=new(StringComparer.Ordinal)
    {
        "version","language","theme","developerMode","countdownSeconds","downCount","showNotifyIcon","showTrayIcon","keepScreenOn",
        "disableFP16","disableTopmost","duplicateFrameDetectionMode","enableStatisticsForDynamicDetection","minFrameRate",
        "scalingModes","profiles","scalingProfiles","shortcuts","hotkeys","overlay"
    };
    private static readonly HashSet<string> ProfileFields=new(StringComparer.Ordinal)
    {
        "name","packaged","pathRule","classNameRule","launcherPath","launchParameters","scalingMode","captureMethod","captureMode",
        "initialWindowedScaleFactor","customInitialWindowedScaleFactor","fullscreenInitialToolbarState","windowedInitialToolbarState",
        "screenshotsDir","screenshotFilenameTemplate","frameRateLimiterEnabled","maxFrameRate","croppingEnabled","cropping","outputAlignment","captureTitleBar","reserveTitleBar","autoHideCursorEnabled","autoHideCursorDelay","cursorScaling","customCursorScaling","cursorInterpolationMode","adjustCursorSpeed"
    };
    private static JsonElement? Find(JsonElement owner,string key,string? legacy=null)
        =>owner.TryGetProperty(key,out var value)?value:legacy is not null&&owner.TryGetProperty(legacy,out value)?value:null;
    private static string Text(JsonElement value)
        =>value.ValueKind==JsonValueKind.String?value.GetString()!:throw new InvalidDataException("Expected a string.");
    private static bool Boolean(JsonElement value)
        =>value.ValueKind is JsonValueKind.True or JsonValueKind.False?value.GetBoolean():throw new InvalidDataException("Expected a boolean.");
    private static int Integer(JsonElement value,int minimum,int maximum)
        =>value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int number)&&number>=minimum&&number<=maximum?number:throw new InvalidDataException($"Expected an integer from {minimum} to {maximum}.");
    private static float Number(JsonElement value)
        =>value.ValueKind==JsonValueKind.Number&&value.TryGetSingle(out float number)&&float.IsFinite(number)?number:throw new InvalidDataException("Expected a finite number.");
    private static void Object(JsonElement value)
    { if(value.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Expected a configuration object."); }
    private static void Preserved(JsonElement owner,ISet<string> mapped,string prefix,List<string> warnings)
    {
        foreach(var property in owner.EnumerateObject())
            if(!mapped.Contains(property.Name))warnings.Add(prefix+property.Name+": preserved in the original document; not applied by this build.");
    }
    internal static OriginalConfigurationResult Parse(string text)
    {
        // Reuse the original-mode parser's byte/depth/duplicate-property checks before accessing any settings.
        var modes=ScalingModeInterchange.Parse(text);
        using var document=JsonDocument.Parse(text.TrimStart('\uFEFF'),new JsonDocumentOptions { AllowTrailingCommas=true,CommentHandling=JsonCommentHandling.Skip,MaxDepth=32 });
        var root=document.RootElement;Object(root);
        var profiles=Find(root,"profiles","scalingProfiles")??throw new InvalidDataException("The original profiles array is missing; this may be a scaling-mode file.");
        if(profiles.ValueKind!=JsonValueKind.Array||profiles.GetArrayLength() is <1 or >101)
            throw new InvalidDataException("An original configuration needs a defaults profile and at most 100 application profiles.");
        var settings=new RuntimeSettings { Modes=modes,OriginalConfiguration=root.Clone() };
        var warnings=new List<string>();
        Preserved(root,GlobalFields,"",warnings);
        if(Find(root,"version") is {} version && Integer(version,0,int.MaxValue)>5)
            warnings.Add("version: newer than the original schema supported by this migration; review preserved fields.");
        if(Find(root,"language") is {} language)
        {
            try { settings.Language=Localization.NormalizePreference(Text(language)); }
            catch(InvalidDataException) { warnings.Add("language: unsupported preference preserved; using the system language."); }
        }
        if(Find(root,"developerMode") is {} developer)settings.DeveloperMode=Boolean(developer);
        if(Find(root,"theme") is {} theme)settings.Theme=Integer(theme,0,2);
        if(Find(root,"countdownSeconds","downCount") is {} countdown)settings.CountdownSeconds=Integer(countdown,1,5);
        if(Find(root,"showNotifyIcon","showTrayIcon") is {} tray)settings.ShowTrayIcon=Boolean(tray);
        if(Find(root,"keepScreenOn") is {} awake)settings.KeepScreenOn=Boolean(awake);
        if(Find(root,"disableFP16") is {} fp16)settings.DisableFP16=Boolean(fp16);
        if(Find(root,"disableTopmost") is {} topmost)settings.DisableTopmost=Boolean(topmost);
        if(Find(root,"duplicateFrameDetectionMode") is {} duplicate)settings.DuplicateFrameDetection=(DuplicateFrameMode)Integer(duplicate,0,2);
        if(Find(root,"enableStatisticsForDynamicDetection") is {} statistics)settings.StatisticsForDynamicDetection=Boolean(statistics);
        if(Find(root,"minFrameRate") is {} minimum)settings.MinimumFrameRate=Number(minimum);
        if(Find(root,"shortcuts","hotkeys") is {} shortcuts)
        {
            Object(shortcuts);
            ReadShortcut(shortcuts,"scale",null,settings.FullscreenShortcut,warnings);
            ReadShortcut(shortcuts,"windowedModeScale",null,settings.WindowedShortcut,warnings);
            ReadShortcut(shortcuts,"toolbar","overlay",settings.ToolbarShortcut,warnings);
            ReadShortcut(shortcuts,"takeScreenshot",null,settings.ScreenshotShortcut,warnings);
            Preserved(shortcuts,new HashSet<string>(["scale","windowedModeScale","toolbar","overlay","takeScreenshot"],StringComparer.Ordinal),"shortcuts.",warnings);
        }
        // Keep original array positions until every profile's numeric reference has been resolved.
        var originalModes=modes.ToArray();EffectModeSettings? unselected=null;
        void ReadProfile(JsonElement source,RuntimeSettings target,string prefix)
        {
            Object(source);Preserved(source,ProfileFields,prefix,warnings);
            int index=Find(source,"scalingMode") is {} selection?Integer(selection,int.MinValue,int.MaxValue):-1;
            if(index<0||index>=originalModes.Length)
            {
                if(unselected is null)
                {
                    string name="Original: no scaling mode selected";
                    for(int suffix=2;modes.Any(m=>m.Name.Equals(name,StringComparison.OrdinalIgnoreCase));suffix++)name="Original: no scaling mode selected ("+suffix+")";
                    unselected=new() { Name=name,Steps=[] };modes.Add(unselected);
                }
                target.SelectedModeId=unselected.Id;
                warnings.Add(prefix+"scalingMode: no valid mode selected; scaling stays unavailable until a mode is chosen.");
            }
            else target.SelectedModeId=originalModes[index].Id;
            int capture=Find(source,"captureMethod","captureMode") is {} method?Integer(method,0,3):0;
            target.CaptureBackend=capture switch { 0=>CaptureBackend.GraphicsCapture,1=>CaptureBackend.DesktopDuplication,2=>CaptureBackend.Gdi,_=>CaptureBackend.DwmSharedSurface };
            if(!CaptureBackendPolicy.Supported(target.CaptureBackend))warnings.Add(prefix+"captureMethod: preserved Windows backend is unavailable on this build; explicitly choose an available backend before capture.");
            if(Find(source,"captureTitleBar","reserveTitleBar") is {} titleBar)target.CaptureTitleBar=Boolean(titleBar);
            if(Find(source,"initialWindowedScaleFactor") is {} initial)target.InitialWindowedScaleFactor=Integer(initial,0,6);
            if(Find(source,"customInitialWindowedScaleFactor") is {} custom)target.CustomInitialWindowedScaleFactor=Number(custom);
            if(Find(source,"outputAlignment") is {} alignment)target.OutputAlignment=Integer(alignment,0,8);
            if(Find(source,"fullscreenInitialToolbarState") is {} fullscreen)target.FullscreenInitialToolbarState=(ToolbarState)Integer(fullscreen,0,2);
            if(Find(source,"windowedInitialToolbarState") is {} windowed)target.WindowedInitialToolbarState=(ToolbarState)Integer(windowed,0,2);
            if(Find(source,"autoHideCursorEnabled") is {} autoHide)target.AutoHideCursorEnabled=Boolean(autoHide);
            if(Find(source,"cursorScaling") is {} cursorScaling)target.CursorScaling=Integer(cursorScaling,0,7);
            if(Find(source,"adjustCursorSpeed") is {} adjustSpeed)target.AdjustCursorSpeed=Boolean(adjustSpeed);
            if(Find(source,"customCursorScaling") is {} customCursor)target.CustomCursorScaleFactor=Number(customCursor);
            if(Find(source,"cursorInterpolationMode") is {} interpolation)target.CursorInterpolation=Integer(interpolation,0,1);
            if(Find(source,"autoHideCursorDelay") is {} hideDelay)target.AutoHideCursorDelay=Number(hideDelay);
            if(Find(source,"frameRateLimiterEnabled") is {} limiter)target.FrameRateLimiterEnabled=Boolean(limiter);
            if(Find(source,"maxFrameRate") is {} maximum)target.MaximumFrameRate=Number(maximum);
            if(Find(source,"croppingEnabled") is {} cropEnabled)target.IsCroppingEnabled=Boolean(cropEnabled);
            if(Find(source,"cropping") is {} crop)
            {
                Object(crop);
                if(Find(crop,"left") is {} left)target.CroppingLeft=Number(left);
                if(Find(crop,"top") is {} top)target.CroppingTop=Number(top);
                if(Find(crop,"right") is {} right)target.CroppingRight=Number(right);
                if(Find(crop,"bottom") is {} bottom)target.CroppingBottom=Number(bottom);
                Preserved(crop,new HashSet<string>(["left","top","right","bottom"],StringComparer.Ordinal),prefix+"cropping.",warnings);
            }
            ReadScreenshots(source,target,prefix,warnings);
        }
        ReadProfile(profiles[0],settings,"profiles[0].");
        for(int i=1;i<profiles.GetArrayLength();i++)
        {
            var source=profiles[i];Object(source);
            string name=Text(Find(source,"name")??throw new InvalidDataException("Application profile name is missing."));
            string pathRule=Text(Find(source,"pathRule")??throw new InvalidDataException("Application path rule is missing."));
            string classRule=Text(Find(source,"classNameRule")??throw new InvalidDataException("Application window class is missing."));
            _=Boolean(Find(source,"packaged")??throw new InvalidDataException("Application package flag is missing."));
            if(string.IsNullOrWhiteSpace(pathRule)||string.IsNullOrWhiteSpace(classRule))throw new InvalidDataException("Application identity rules cannot be empty.");
            var profile=new ApplicationProfile { Name=name,OriginalProfile=source.Clone() };
            // Profiles start with original defaults, rather than inheriting another app's or the default profile's options.
            var globals=settings.CopyForProfile();
            profile.Settings=new RuntimeSettings { Modes=modes,Language=globals.Language,Theme=globals.Theme,
                FullscreenShortcut=globals.FullscreenShortcut,WindowedShortcut=globals.WindowedShortcut,ToolbarShortcut=globals.ToolbarShortcut,ScreenshotShortcut=globals.ScreenshotShortcut,
                KeepScreenOn=globals.KeepScreenOn,DisableFP16=globals.DisableFP16,DisableTopmost=globals.DisableTopmost,DuplicateFrameDetection=globals.DuplicateFrameDetection,
                StatisticsForDynamicDetection=globals.StatisticsForDynamicDetection,MinimumFrameRate=globals.MinimumFrameRate,
                ShowTrayIcon=globals.ShowTrayIcon,CountdownSeconds=globals.CountdownSeconds,DeveloperMode=globals.DeveloperMode };
            ReadProfile(source,profile.Settings,$"profiles[{i}].");settings.Profiles.Add(profile);
            warnings.Add($"profiles[{i}]: Windows application rules and raw launch parameters preserved; select and review a local executable and arguments before launching. Automatic matching is not migrated.");
        }
        if(Find(root,"overlay") is {} overlay)
        {
            Object(overlay);bool legacy=Find(overlay,"fullscreenInitialToolbarState","initialToolbarState") is not null||Find(overlay,"screenshotsDir") is not null;
            if(legacy)
            {
                if(Find(overlay,"fullscreenInitialToolbarState") is {} full)
                {
                    settings.FullscreenInitialToolbarState=(ToolbarState)Integer(full,0,2);
                    if(Find(overlay,"windowedInitialToolbarState") is {} window)settings.WindowedInitialToolbarState=(ToolbarState)Integer(window,0,2);
                }
                else if(Find(overlay,"initialToolbarState") is {} both)settings.FullscreenInitialToolbarState=settings.WindowedInitialToolbarState=(ToolbarState)Integer(both,0,2);
                ReadScreenshots(overlay,settings,"overlay.",warnings);
                foreach(var profile in settings.Profiles)
                {
                    profile.Settings.FullscreenInitialToolbarState=settings.FullscreenInitialToolbarState;
                    profile.Settings.WindowedInitialToolbarState=settings.WindowedInitialToolbarState;
                    profile.Settings.ScreenshotDirectory=settings.ScreenshotDirectory;
                }
            }
            if(Find(overlay,"windows") is {} windows)
            {
                Object(windows);
                if(Find(windows,"profiler") is {} profiler)
                {
                    Object(profiler);
                    settings.ProfilerPlacement=new OverlayPlacement(
                        Find(profiler,"hArea") is {} h?Integer(h,0,2):2,
                        Find(profiler,"vArea") is {} v?Integer(v,0,2):1,
                        Find(profiler,"hPos") is {} x?x.GetDouble():60,
                        Find(profiler,"vPos") is {} y?y.GetDouble():.5);
                    settings.ProfilerPlacement.Validate();
                    Preserved(profiler,new HashSet<string>(["hArea","vArea","hPos","vPos"],StringComparer.Ordinal),"overlay.windows.profiler.",warnings);
                }
                Preserved(windows,new HashSet<string>(["profiler"],StringComparer.Ordinal),"overlay.windows.",warnings);
            }
            Preserved(overlay,new HashSet<string>(["fullscreenInitialToolbarState","windowedInitialToolbarState","initialToolbarState","screenshotsDir","windows"],StringComparer.Ordinal),"overlay.",warnings);
        }
        if(modes.Any(m=>!m.IsAvailable))warnings.Add("scalingModes: empty modes or unsupported effects remain unavailable; original effect data is preserved.");
        settings.Validate();return new(settings,warnings);
    }
    private static void ReadScreenshots(JsonElement source,RuntimeSettings settings,string prefix,List<string> warnings)
    {
        if(Find(source,"screenshotsDir") is {} directory)
        {
            string path=Text(directory);
            // A Windows path, or a relative path from a different installation, has no equivalent host location.
            if(path.Length==0)settings.ScreenshotDirectory="";
            else if(OperatingSystem.IsWindows()&&Path.IsPathFullyQualified(path))settings.ScreenshotDirectory=path;
            else warnings.Add(prefix+"screenshotsDir: original location preserved; select a local screenshot directory (using the default for now).");
        }
        if(Find(source,"screenshotFilenameTemplate") is {} template)
        {
            string value=Text(template);
            if(value.Length<=1024&&ScreenshotNames.IsValidTemplate(value))settings.ScreenshotTemplate=value;
            else warnings.Add(prefix+"screenshotFilenameTemplate: invalid template preserved; using the default template.");
        }
    }
    private static void ReadShortcut(JsonElement source,string key,string? legacy,GlobalShortcutBinding target,List<string> warnings)
    {
        if(Find(source,key,legacy) is not {} encoded)return;
        int value=Integer(encoded,0,0xfff),vk=value&255;
        string? name=vk switch
        {
            >=0x41 and <=0x5a=>((char)vk).ToString(),>=0x30 and <=0x39=>"D"+(char)vk,
            >=0x60 and <=0x69=>"NumPad"+(vk-0x60),>=0x70 and <=0x87=>"F"+(vk-0x6f),
            8=>"Back",9=>"Tab",13=>"Enter",19=>"Pause",20=>"CapsLock",27=>"Escape",32=>"Space",
            33=>"PageUp",34=>"PageDown",35=>"End",36=>"Home",37=>"Left",38=>"Up",39=>"Right",40=>"Down",44=>"Print",45=>"Insert",46=>"Delete",
            106=>"Multiply",107=>"Add",109=>"Subtract",110=>"Decimal",111=>"Divide",144=>"NumLock",145=>"Scroll",
            186=>"OemSemicolon",187=>"OemPlus",188=>"OemComma",189=>"OemMinus",190=>"OemPeriod",191=>"OemQuestion",192=>"OemTilde",
            219=>"OemOpenBrackets",220=>"OemPipe",221=>"OemCloseBrackets",222=>"OemQuotes",_=>null
        };
        string modifiers=string.Join(',',new[]{(0x200,"Ctrl"),(0x400,"Alt"),(0x800,"Shift"),(0x100,"Win")}.Where(pair=>(value&pair.Item1)!=0).Select(pair=>pair.Item2));
        if(name is null||modifiers.Length==0)
        { warnings.Add("shortcuts."+key+": disabled or unsupported binding preserved; preference uses the default and requires explicit registration.");return; }
        target.Key=name;target.Modifiers=modifiers;
    }
    internal static void ValidatePreserved(JsonElement value)
    {
        Object(value);
        string raw=value.GetRawText();
        if(System.Text.Encoding.UTF8.GetByteCount(raw)>ScalingModeInterchange.MaximumBytes)throw new InvalidDataException("Preserved original configuration exceeds 4 MiB.");
        using var document=JsonDocument.Parse(raw,new JsonDocumentOptions { MaxDepth=32,CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true });
        ScalingModeInterchange.CheckObjects(document.RootElement);
    }
    internal static async Task<int> RunAsync(string source,string destination)
    {
        try
        {
            await using var input=File.OpenRead(source);
            var result=Parse(await ScalingModeInterchange.ReadAsync(input));
            string path=Path.GetFullPath(destination);
            if(File.Exists(path)||Directory.Exists(path))throw new IOException("The destination already exists. Choose a new file; migration never overwrites existing settings.");
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { result.Settings.WriteFile(temporary);File.Move(temporary,path,false); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
            Console.WriteLine($"Migrated {result.Settings.Profiles.Count} application profiles and {result.Settings.Modes.Count} modes to {path}");
            foreach(string warning in result.Warnings)Console.WriteLine("Review: "+warning);
            return 0;
        }
        catch(Exception failure) { Console.Error.WriteLine("Configuration migration failed: "+failure.Message);return 1; }
    }
}
