using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Magpie.Desktop.Core;

internal sealed class EngineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public EngineHandle() : base(true) { }
    protected override bool ReleaseHandle() { NativeCore.Destroy(handle); return true; }
}
internal sealed class CaptureHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public CaptureHandle() : base(true) { }
    protected override bool ReleaseHandle() { NativeCore.CaptureDestroy(handle); return true; }
}
internal static class NativeCore
{
    private const string Library = "magpie_native";
    [DllImport(Library, EntryPoint = "mp_last_error")] private static extern nint LastError();
    internal static Exception Error() => new InvalidOperationException(Marshal.PtrToStringUTF8(LastError()) ?? "Native core failed.");
    [DllImport(Library, EntryPoint = "mp_create")] internal static extern EngineHandle Create();
    [DllImport(Library, EntryPoint = "mp_create_for_device")] internal static extern EngineHandle CreateForDevice([MarshalAs(UnmanagedType.LPUTF8Str)] string identifier);
    [DllImport(Library, EntryPoint = "mp_create_with_precision")] internal static extern EngineHandle CreateWithPrecision([MarshalAs(UnmanagedType.LPUTF8Str)] string identifier,uint precision);
    [DllImport(Library, EntryPoint = "mp_fp16_supported")] internal static extern int FP16Supported(EngineHandle engine);
    [DllImport(Library, EntryPoint = "mp_effect_uses_fp16")] internal static extern int EffectUsesFP16(EngineHandle engine,uint effect);
    internal static EngineHandle CreateForSettings(RuntimeSettings settings)=>CreateWithPrecision(settings.GraphicsDeviceId,RuntimeSettings.Current.DisableFP16?0u:1u);
    [DllImport(Library, EntryPoint = "mp_device_identifier")] internal static extern nint DeviceIdentifier(EngineHandle engine);
    [DllImport(Library, EntryPoint = "mp_devices_create")] internal static extern DeviceListHandle DevicesCreate();
    [DllImport(Library, EntryPoint = "mp_devices_destroy")] internal static extern void DevicesDestroy(nint devices);
    [DllImport(Library, EntryPoint = "mp_devices_count")] internal static extern uint DevicesCount(DeviceListHandle devices);
    [DllImport(Library, EntryPoint = "mp_devices_get")] internal static extern int DevicesGet(DeviceListHandle devices,uint index,out NativeDeviceInfo info);
    [DllImport(Library, EntryPoint = "mp_destroy")] internal static extern void Destroy(nint engine);
    [DllImport(Library, EntryPoint = "mp_device_name")] internal static extern nint DeviceName(EngineHandle engine);
    [DllImport(Library, EntryPoint = "mp_scale")] internal static extern unsafe int Scale(EngineHandle engine, nint input, uint width, uint height, byte* output, uint outWidth, uint outHeight, uint mode, float antiRinging);
    [DllImport(Library, EntryPoint = "mp_scale_ex")] internal static extern unsafe int ScaleExtended(EngineHandle engine, nint input, uint width, uint height, byte* output, uint outWidth, uint outHeight, uint mode, float parameter1, float parameter2);
    [DllImport(Library, EntryPoint = "mp_scale_chain_configured_v2")] internal static extern unsafe int ScaleChain(EngineHandle engine, nint input, uint width, uint height, byte* output, ulong outputBytes, NativeEffectStep* steps, uint count);
    [DllImport(Library, EntryPoint = "mp_set_gpu_profiling")] internal static extern int SetGpuProfiling(EngineHandle engine,int enabled);
    [DllImport(Library, EntryPoint = "mp_get_gpu_profile_v2")] internal static extern int GetGpuProfile(EngineHandle engine,out NativeGpuProfile profile);
    [DllImport(Library, EntryPoint = "mp_capture_create")] internal static extern CaptureHandle CaptureCreate();
    [DllImport(Library, EntryPoint = "mp_capture_destroy")] internal static extern void CaptureDestroy(nint capture);
    [DllImport(Library, EntryPoint = "mp_capture_start")] internal static extern int CaptureStart(CaptureHandle capture);
    [DllImport(Library, EntryPoint = "mp_capture_cancel")] internal static extern void CaptureCancel(CaptureHandle capture);
    [DllImport(Library, EntryPoint = "mp_capture_frame")] internal static extern int CaptureFrame(CaptureHandle capture, out nint rgba, out uint width, out uint height);
    [DllImport(Library, EntryPoint = "mp_capture_configure_input")] internal static extern int CaptureConfigureInput(CaptureHandle capture,nint window,
        double x,double y,double width,double height,double cropX,double cropY,double cropWidth,double cropHeight,double toolbarHeight,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string reservedCharacter,int reservedCode,ulong reservedModifiers);

    public static unsafe int SmokeTest()
    {
        NativeGlobalShortcutTests.Run();
        GlobalShortcutSettingsTests.Run();
        StartupRegistrationTests.Run();
        ScreenAwakeTests.Verify();
        VerifyLocalization();
        var settings = new RuntimeSettings();
        if (settings.OutputSize(640, 480, 1920, 1080) != (1440u,1080u)) throw new InvalidOperationException("Fit dimensions are incorrect.");
        settings.ScaleType = 0; settings.ScaleX = 2; settings.ScaleY = 3;
        if (settings.OutputSize(640,480,1920,1080) != (1280u,1440u)) throw new InvalidOperationException("Factor dimensions are incorrect.");
        settings.ScaleType = 2; settings.AbsoluteWidth = 800; settings.AbsoluteHeight = 600;
        if (settings.OutputSize(640,480,1920,1080) != (800u,600u)) throw new InvalidOperationException("Absolute dimensions are incorrect.");
        settings.ScaleType = 3;
        if (settings.OutputSize(640,480,1920,1080) != (1920u,1080u)) throw new InvalidOperationException("Fill dimensions are incorrect.");
        settings.BicubicB = .2f; settings.BicubicC = .7f;
        string json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.RuntimeSettings);
        var restored = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.RuntimeSettings)!;
        restored.Validate();
        if (restored.ScaleX != 2 || restored.AbsoluteWidth != 800 || restored.BicubicC != .7f || restored.BicubicB != .2f) throw new InvalidOperationException("AOT settings JSON roundtrip failed.");
        restored.LanczosAntiRinging = float.NaN;
        try { restored.Validate(); throw new InvalidOperationException("Invalid parameter was accepted."); } catch (InvalidDataException) { }
        var mode = new EffectModeSettings { Name = "Test chain", Steps = [
            new() { Effect = 1, ScaleType = 0, ScaleX = 2, ScaleY = 2 },
            new() { Effect = 3, ScaleType = 2, Width = 18, Height = 14, Parameter1 = .2f, Parameter2 = .7f }
        ] };
        var resolved = mode.Resolve(7,5,1920,1080);
        if (resolved[0].Width != 14 || resolved[0].Height != 10 || resolved[1].Width != 18 || resolved[1].Parameter2 != .7f)
            throw new InvalidOperationException("Effect chain dimension or parameter resolution failed.");
        var duplicate = mode.Duplicate("Copy");
        duplicate.Steps[0].Parameter1 = .1f;
        if (mode.Steps[0].Parameter1 == .1f || duplicate.Id == mode.Id) throw new InvalidOperationException("Mode copy shares mutable state.");
        settings.Modes.Add(mode); settings.SelectedModeId=mode.Id;
        string modeJson = System.Text.Json.JsonSerializer.Serialize(settings,SettingsJson.Default.RuntimeSettings);
        var loadedMode = System.Text.Json.JsonSerializer.Deserialize(modeJson,SettingsJson.Default.RuntimeSettings)!;
        loadedMode.Validate();
        if (loadedMode.Modes[0].Steps.Count != 2 || loadedMode.ResolveChain(7,5,1920,1080)[1].Parameter2 != .7f)
            throw new InvalidOperationException("AOT effect chain serialization failed.");
        if(Marshal.SizeOf<NativeEffectStep>()!=84)throw new InvalidOperationException("Configured stage ABI size mismatch.");
        var crtSettings=new RuntimeSettings { Effect=12 };
        crtSettings.Crt[7]=.12f;crtSettings.Crt[12]=2;
        var crtCopy=crtSettings.CopyForProfile();crtCopy.Crt[7]=-.1f;
        if(crtSettings.Crt[7]!=.12f)throw new InvalidOperationException("CRT application copy shares parameter storage.");
        var crtRoundtrip=System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(crtSettings,SettingsJson.Default.RuntimeSettings),SettingsJson.Default.RuntimeSettings)!;
        crtRoundtrip.Validate();var crtPlan=crtRoundtrip.ResolveChain(7,5,19,13);
        if(crtPlan[0].Extra[7]!=.12f || crtPlan[0].Extra[12]!=2)throw new InvalidOperationException("CRT parameters lost during serialization or ABI resolution.");
        var crtStage=new EffectStageSettings { Effect=12 };var crtStageCopy=crtStage.Clone();crtStageCopy.Crt[0]=1;
        if(crtStage.Crt[0]!=2.4f)throw new InvalidOperationException("CRT stage copy shares parameter storage.");
        crtRoundtrip.Crt[12]=1.5f;
        try { crtRoundtrip.Validate();throw new InvalidOperationException("Fractional CRT integer parameter accepted."); } catch(InvalidDataException) { }
        var fsr = new RuntimeSettings { Effect = 6, FsrSharpness = .41f };
        var fsrChain = fsr.ResolveChain(7,5,190,130);
        if (fsrChain.Length != 2 || fsrChain[0].Effect != 4 || fsrChain[1].Effect != 5 || fsrChain[1].Parameter1 != .41f || fsrChain[1].Width != fsrChain[0].Width)
            throw new InvalidOperationException("Built-in FSR chain resolution failed.");
        var sameSize = new EffectStageSettings { Effect = 5, ScaleType = 3 }.Resolve(7,5,190,130);
        if (sameSize.Width != 7 || sameSize.Height != 5) throw new InvalidOperationException("RCAS should preserve input dimensions.");
        var fsrJson = System.Text.Json.JsonSerializer.Serialize(fsr,SettingsJson.Default.RuntimeSettings);
        if (System.Text.Json.JsonSerializer.Deserialize(fsrJson,SettingsJson.Default.RuntimeSettings)!.ResolveChain(7,5,190,130)[1].Parameter1 != .41f)
            throw new InvalidOperationException("AOT FSR settings serialization failed.");
        var timestamp = new DateTime(2026, 10, 20, 9, 8, 7, 6);
        if (ScreenshotNames.Format("%WT:2%_%PN%_%Y%-%y%-%m%-%D%_%H%-%M%-%S%-%MS%", "中文窗口", "app", timestamp) != "中文_app_2026-26-10-20_09-08-07-006.png")
            throw new InvalidOperationException("Screenshot filename token expansion failed.");
        if (ScreenshotNames.Format("../%WT%", "bad/name", "", timestamp).Contains('/'))
            throw new InvalidOperationException("Screenshot filename escaped its directory.");
        settings.ScreenshotDirectory=Path.Combine(Path.GetTempPath(),"magpie-settings-test");
        settings.ScreenshotTemplate="测试_%WT:2%_\\%_%MS%";
        var screenshotJson=System.Text.Json.JsonSerializer.Serialize(settings,SettingsJson.Default.RuntimeSettings);
        var screenshotSettings=System.Text.Json.JsonSerializer.Deserialize(screenshotJson,SettingsJson.Default.RuntimeSettings)!;
        screenshotSettings.Validate();
        if(screenshotSettings.ResolveScreenshotDirectory()!=settings.ScreenshotDirectory || screenshotSettings.ResolveScreenshotTemplate()!=settings.ScreenshotTemplate)
            throw new InvalidOperationException("Screenshot settings JSON roundtrip failed.");
        string persistenceRoot=Path.Combine(Path.GetTempPath(),"magpie-settings-"+Guid.NewGuid().ToString("N"));
        try
        {
            string path=Path.Combine(persistenceRoot,"settings.json");
            screenshotSettings.WriteFile(path);
            screenshotSettings.ScreenshotTemplate="changed_%MS%";
            screenshotSettings.WriteFile(path);
            var fromDisk=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;
            fromDisk.Validate();
            if(fromDisk.ScreenshotTemplate!="changed_%MS%" || fromDisk.ScreenshotDirectory!=settings.ScreenshotDirectory || Directory.GetFiles(persistenceRoot).Length!=1)
                throw new InvalidOperationException("Atomic screenshot settings save/reload failed.");
            try { screenshotSettings.WriteFile(Path.Combine(path,"blocked.json"));throw new InvalidOperationException("Settings path collision accepted."); } catch(IOException) { }
            if(System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!.ScreenshotTemplate!="changed_%MS%")
                throw new InvalidOperationException("Failed settings save damaged existing configuration.");
        }
        finally { if(Directory.Exists(persistenceRoot))Directory.Delete(persistenceRoot,true); }
        screenshotSettings.ScreenshotDirectory="relative/path";
        try { screenshotSettings.Validate();throw new InvalidOperationException("Relative screenshot directory accepted."); } catch(InvalidDataException) { }
        screenshotSettings.ScreenshotDirectory="";screenshotSettings.ScreenshotTemplate="../bad";
        try { screenshotSettings.Validate();throw new InvalidOperationException("Invalid filename template accepted."); } catch(InvalidDataException) { }
        foreach(string invalid in new[]{"%WT:0%","%WT:abc%","bad/name","bad\0name"})
            if(ScreenshotNames.IsValidTemplate(invalid))throw new InvalidOperationException("Invalid template accepted: "+invalid);
        foreach(string valid in new[]{"","%WT:999%","%PN:2%_%Y%","\\%_%MS%"})
            if(!ScreenshotNames.IsValidTemplate(valid))throw new InvalidOperationException("Valid template rejected: "+valid);
        string unicodeName=ScreenshotNames.Format("%WT%",string.Concat(Enumerable.Repeat("窗口😀",120)),"app",timestamp);
        if(System.Text.Encoding.UTF8.GetByteCount(unicodeName)>240 || unicodeName.Contains('\uFFFD'))
            throw new InvalidOperationException("Unicode screenshot filename exceeds component limit or splits a rune.");
        if(ScreenshotNames.Format("CON","","",timestamp)!="_CON.png" || ScreenshotNames.Format("\\%_%Y%","","",timestamp)!="%_2026.png")
            throw new InvalidOperationException("Screenshot reserved name or percent escaping failed.");
        var pacingSettings=new RuntimeSettings { FrameRateLimiterEnabled=true,MaximumFrameRate=29.97 };
        var pacingJson=System.Text.Json.JsonSerializer.Serialize(pacingSettings,SettingsJson.Default.RuntimeSettings);
        if(System.Text.Json.JsonSerializer.Deserialize(pacingJson,SettingsJson.Default.RuntimeSettings)!.EffectiveMaximumFrameRate!=29.97)
            throw new InvalidOperationException("Frame rate configuration JSON roundtrip failed.");
        foreach(double invalidRate in new[]{0d,9d,1001d,double.NaN,double.PositiveInfinity})
        {
            pacingSettings.MaximumFrameRate=invalidRate;
            try { pacingSettings.Validate();throw new InvalidOperationException("Invalid maximum frame rate accepted."); } catch(InvalidDataException) { }
        }
        VerifyFramePacingAsync().GetAwaiter().GetResult();
        VerifyProfiles();
        using var engine = Create();
        if (engine.IsInvalid) throw Error();
        VerifyGraphicsDevices(engine);
        FrameProcessingTests.Verify(engine);
        ScalingModeInterchangeTests.Verify(engine);
        PresentationPlanTests.Verify(engine);
        WindowSizingTests.Verify();
        SourceCroppingTests.Verify(engine);
        Nnedi3Tests.Verify(engine);
        SsimTests.Verify(engine);
        AnimeExpandedTests.Verify(engine);
        RavuTests.Verify(engine);
        SmaaTests.Verify(engine);
        GraphFilterTests.Verify(engine);
        GraphCatalogTests.Verify();
        PrecisionTests.Verify();
        byte[] input = [25, 50, 75, 255];
        byte[] output = new byte[18 * 14 * 4];
        fixed (byte* src = input, dst = output)
            if (Scale(engine, (nint)src, 1, 1, dst, 18, 14, 1, .5f) == 0) throw Error();
        for (var i = 0; i < output.Length; i++)
            if (output[i] != input[i % 4]) throw new InvalidOperationException("Vulkan output pixels are incorrect.");
        fixed (byte* src = input, dst = output)
            if (ScaleExtended(engine, (nint)src, 1, 1, dst, 18, 14, 3, .2f, .7f) == 0) throw Error();
        for (var i = 0; i < output.Length; i++)
            if (Math.Abs(output[i] - input[i % 4]) > 1) throw new InvalidOperationException("Bicubic extended parameter binding failed.");
        fixed (byte* src = input, dst = output)
        fixed (NativeEffectStep* steps = resolved)
            if (ScaleChain(engine,(nint)src,1,1,dst,(ulong)output.Length,steps,(uint)resolved.Length) == 0) throw Error();
        for (var i=0;i<output.Length;i++) if(Math.Abs(output[i]-input[i%4])>1) throw new InvalidOperationException("AOT GPU chain output differs.");
        NativeEffectStep[] fsrSmoke = [new(){Effect=4,Width=18,Height=14},new(){Effect=5,Width=18,Height=14,Parameter1=.87f}];
        fixed (byte* src = input, dst = output)
        fixed (NativeEffectStep* steps = fsrSmoke)
            if (ScaleChain(engine,(nint)src,1,1,dst,(ulong)output.Length,steps,2)==0) throw Error();
        for(var i=0;i<output.Length;i++)if(Math.Abs(output[i]-input[i%4])>1)throw new InvalidOperationException("AOT original FSR compute chain failed.");
        var crtSmoke=new RuntimeSettings { Effect=12,ScaleType=2,AbsoluteWidth=19,AbsoluteHeight=13 }.ResolveChain(1,1,19,13);
        var crtPixels=new byte[19*13*4];
        fixed(NativeEffectStep* stage=crtSmoke)fixed(byte* target=crtPixels)fixed(byte* source=input)
            if(ScaleChain(engine,(nint)source,1,1,target,(ulong)crtPixels.Length,stage,1)==0)throw Error();
        if(crtPixels.Where((_,i)=>i%4==3).Any(a=>a!=255))throw new InvalidOperationException("Configured CRT AOT output is incomplete.");
        foreach(uint cnn in new[]{7u,8u,9u,10u,11u,13u})
        {
            var cnnSettings=new RuntimeSettings { Effect=cnn };
            var cnnChain=cnnSettings.ResolveChain(1,1,190,130);
            if(cnnChain.Length!=1 || cnnChain[0].Width!=2 || cnnChain[0].Height!=2 || cnnChain[0].Effect!=cnn)throw new InvalidOperationException("CNN 2x dimensions are incorrect.");
            byte[] cnnOutput=new byte[16];
            fixed(byte* src=input,dst=cnnOutput)
            fixed(NativeEffectStep* steps=cnnChain)
                if(ScaleChain(engine,(nint)src,1,1,dst,16,steps,1)==0)throw Error();
            for(int i=0;i<16;i++)if(Math.Abs(cnnOutput[i]-input[i%4])>1)throw new InvalidOperationException("AOT CNN constant CPU reference differs.");
        }
        var oversizedCnn=new EffectModeSettings { Steps=[new(){Effect=8},new(){Effect=8}] };
        try { oversizedCnn.Resolve(1024,1024,4096,4096);throw new InvalidOperationException("CNN intermediate memory budget was ignored."); } catch(InvalidOperationException error) when(error.Message.Contains("resource limit")) { }
        Console.WriteLine($"CORE PASS: settings persistence, screenshot names, Vulkan effects/chains, device={Marshal.PtrToStringUTF8(DeviceName(engine))}");
        return 0;
    }
    private static void VerifyLocalization()
    {
        Localization.VerifyResources();
        var settings=new RuntimeSettings { Language="zh-Hans" };
        string directory=Path.Combine(Path.GetTempPath(),"magpie-language-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            string file=Path.Combine(directory,"folder with spaces","language.json");settings.WriteFile(file);
            var restored=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(file),SettingsJson.Default.RuntimeSettings)!;restored.Validate();
            if(restored.Language!="zh-Hans")throw new InvalidOperationException("Language disk roundtrip failed.");
            var arguments=new[]{"--localization-probe",file};
            var command=ApplicationRestart.CurrentStartInfo(arguments);command.RedirectStandardOutput=true;command.RedirectStandardError=true;
            using var child=System.Diagnostics.Process.Start(command) ?? throw new InvalidOperationException("Language startup probe could not start.");
            var readOutput=child.StandardOutput.ReadToEndAsync();var readError=child.StandardError.ReadToEndAsync();
            if(!child.WaitForExit(30000)) { child.Kill();throw new InvalidOperationException("Language startup probe timed out."); }
            string output=readOutput.GetAwaiter().GetResult(),error=readError.GetAwaiter().GetResult();
            if(child.ExitCode!=0)throw new InvalidOperationException("Language startup probe failed: "+error);
            var probe=System.Text.Json.JsonSerializer.Deserialize(output,LocalizationProbeJson.Default.LocalizationProbeResult)!;
            if(probe.Language!="zh-Hans" || probe.Home!="主页" || probe.Defaults!=LocalizationText("zh-Hans","Root_Defaults.Content"))throw new InvalidOperationException("Replacement process did not load the saved language.");
            settings.Language="not-supported";
            try { settings.WriteFile(file);throw new InvalidOperationException("Unsupported language saved."); } catch(InvalidDataException) { }
            if(System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(file),SettingsJson.Default.RuntimeSettings)!.Language!="zh-Hans")throw new InvalidOperationException("Rejected language save damaged settings.");
            var native=ApplicationRestart.BuildStartInfo(Path.GetFullPath(Path.Combine(directory,"Magpie.Desktop")),null,new[]{"one argument","$(literal)","\"quoted\""});
            if(native.UseShellExecute || native.ArgumentList.Count!=3 || native.ArgumentList[1]!="$(literal)")throw new InvalidOperationException("Native restart arguments are not literal.");
            var managed=ApplicationRestart.BuildStartInfo(Path.GetFullPath(Path.Combine(directory,"dotnet")),Path.GetFullPath(Path.Combine(directory,"Magpie.Desktop.dll")),arguments);
            if(managed.ArgumentList.Count!=3 || !managed.ArgumentList[0].EndsWith("Magpie.Desktop.dll"))throw new InvalidOperationException("Managed restart command is incorrect.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("LOCALIZATION PASS: 20 embedded resources, English fallback, system culture mapping, persisted choice and new-process startup without UI.");
    }
    private static string LocalizationText(string language,string key)
    {
        string previous=Localization.CurrentPreference;
        try { Localization.Configure(language);return Localization.Get(key); }
        finally { Localization.Configure(previous); }
    }
    private static unsafe void VerifyGraphicsDevices(EngineHandle automatic)
    {
        if(Marshal.SizeOf<NativeDeviceInfo>()!=304)throw new InvalidOperationException("Device enumeration ABI size mismatch.");
        var devices=VulkanDevices.Enumerate();
        string id=Marshal.PtrToStringUTF8(DeviceIdentifier(automatic)) ?? "";
        if(devices.Length==0 || devices.All(d=>d.Identifier!=id))throw new InvalidOperationException("Automatic engine is absent from its device catalog.");
        var selectedSettings=new RuntimeSettings { GraphicsDeviceId=id };
        var copy=selectedSettings.CopyForProfile();copy.GraphicsDeviceId="";
        var json=System.Text.Json.JsonSerializer.Serialize(selectedSettings,SettingsJson.Default.RuntimeSettings);
        var loaded=System.Text.Json.JsonSerializer.Deserialize(json,SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
        if(loaded.GraphicsDeviceId!=id || selectedSettings.GraphicsDeviceId!=id || copy.GraphicsDeviceId!="")throw new InvalidOperationException("GPU selection copy or JSON persistence failed.");
        string directory=Path.Combine(Path.GetTempPath(),"magpie-device-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            string file=Path.Combine(directory,"settings.json");loaded.WriteFile(file);
            var disk=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(file),SettingsJson.Default.RuntimeSettings)!;disk.Validate();
            if(disk.GraphicsDeviceId!=id)throw new InvalidOperationException("GPU selection was not saved to disk.");
            loaded.GraphicsDeviceId="invalid";
            try { loaded.WriteFile(file);throw new InvalidOperationException("Invalid device identifier saved."); } catch(InvalidDataException) { }
            if(System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(file),SettingsJson.Default.RuntimeSettings)!.GraphicsDeviceId!=id)throw new InvalidOperationException("Rejected GPU selection damaged settings.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        using var selected=CreateForDevice(id);
        if(selected.IsInvalid || Marshal.PtrToStringUTF8(DeviceIdentifier(selected))!=id)throw Error();
        byte[] input=[17,41,83,255],output=new byte[16];
        fixed(byte* source=input,target=output)if(Scale(selected,(nint)source,1,1,target,2,2,0,0)==0)throw Error();
        if(output.Where((value,index)=>value!=input[index%4]).Any())throw new InvalidOperationException("Selected AOT device did not dispatch correctly.");
        string absent=new('f',32);
        while(devices.Any(d=>d.Identifier==absent))absent=absent[1..]+"e";
        using var unavailable=CreateForDevice(absent);
        if(!unavailable.IsInvalid)throw new InvalidOperationException("Unavailable GPU silently fell back.");
        foreach(string invalid in new[]{"x",new string('0',31),new string('0',33),new string('g',32)})
        {
            try { VulkanDevices.ValidateIdentifier(invalid);throw new InvalidOperationException("Invalid GPU identifier accepted."); } catch(InvalidDataException) { }
        }
        Console.WriteLine($"DEVICES PASS: {devices.Length} adapters, stable UUID, selected engine, dispatch, independent copy, disk roundtrip, missing GPU rejection.");
    }
    private static async Task VerifyFramePacingAsync()
    {
        var pacer=new FramePacer();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<8;i++) { await pacer.WaitAsync(100,timeout.Token);pacer.MarkFrame(); }
        if(clock.Elapsed.TotalMilliseconds<65)throw new InvalidOperationException("Frame cap permitted frames above its limit.");
        await pacer.WaitAsync(0,timeout.Token); // Disabling discards the previous deadline.
        if(!pacer.WaitAsync(10,timeout.Token).IsCompletedSuccessfully)throw new InvalidOperationException("Disabling pacing retained a deadline.");
        pacer.MarkFrame();
        using var stop=new CancellationTokenSource();stop.CancelAfter(10);
        try { await pacer.WaitAsync(10,stop.Token);throw new InvalidOperationException("Frame pacing ignored cancellation."); } catch(OperationCanceledException) { }
        Console.WriteLine("PACING PASS: monotonic 100 FPS cap, disabled reset, cancellable wait.");
    }

    private static void VerifyProfiles()
    {
        bool persistence=RuntimeSettings.PersistenceEnabled;RuntimeSettings.PersistenceEnabled=false;
        string directory=Path.Combine(Path.GetTempPath(),"magpie-profiles-"+Guid.NewGuid().ToString("N"));
        try
        {
            var root=new RuntimeSettings { Effect=6 };
            root.Modes.Add(new EffectModeSettings { Name="Shared mode" });
            var first=root.AddProfile("Alpha",Path.Combine(directory,"app one"));
            first.Settings.Effect=0;first.Settings.MaximumFrameRate=29.97;first.Settings.FrameRateLimiterEnabled=true;
            first.Arguments=["--name","value with spaces","$(literal)","\"quoted\""];
            var second=root.AddProfile("Beta",Path.Combine(directory,"app two"),first.Id);
            second.Settings.FsrSharpness=.12f;
            if(root.Effect!=6 || first.Settings.FsrSharpness==.12f || second.Settings.Effect!=0 || second.Settings.EffectiveMaximumFrameRate!=29.97 || second.Arguments.Count!=4 || ReferenceEquals(first.Arguments,second.Arguments))
                throw new InvalidOperationException("Profile copies share settings or lost fields.");
            root.UseProfile(second.Id);
            if(!ReferenceEquals(root.ActiveSettings,second.Settings) || root.ActiveSettings.ResolveChain(7,5,70,50)[0].Effect!=0)
                throw new InvalidOperationException("Selected application configuration was ignored.");
            root.RenameProfile(first.Id,"Renamed");root.MoveProfile(second.Id,-1);
            if(root.Profiles[0]!=second || first.Name!="Renamed")throw new InvalidOperationException("Profile rename/order failed.");
            try { root.RenameProfile(first.Id,"beta");throw new InvalidOperationException("Duplicate profile name accepted."); } catch(InvalidDataException) { }
            if(first.Name!="Renamed")throw new InvalidOperationException("Invalid rename was not rolled back.");
            if(!ReferenceEquals(first.Settings.Modes,root.Modes) || !ReferenceEquals(second.Settings.Modes,root.Modes))
                throw new InvalidOperationException("Profiles do not share global effect definitions.");
            var launch=first.CreateLaunchInfo();
            if(launch.UseShellExecute || launch.ArgumentList.Count!=4 || launch.ArgumentList[1]!="value with spaces" || launch.ArgumentList[2]!="$(literal)" || !first.MatchesExecutable(first.ExecutablePath))
                throw new InvalidOperationException("Executable matching or literal argument construction failed.");
            string path=Path.Combine(directory,"settings.json");root.WriteFile(path);
            var loaded=System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)!;loaded.Validate();
            if(loaded.Profiles.Count!=2 || loaded.Profiles[0].Id!=second.Id || loaded.ActiveSettings.MaximumFrameRate!=29.97 || loaded.Profiles[1].Arguments[2]!="$(literal)" || !ReferenceEquals(loaded.ActiveSettings.Modes,loaded.Modes))
                throw new InvalidOperationException("Profiles did not survive disk roundtrip.");
            loaded.DeleteProfile(second.Id);
            if(loaded.ActiveProfileId is not null || !ReferenceEquals(loaded.ActiveSettings,loaded) || loaded.Profiles.Count!=1)
                throw new InvalidOperationException("Deleting selected profile did not restore defaults.");
            var nested=loaded.Profiles[0].Settings;nested.Profiles.Add(new ApplicationProfile());
            try { loaded.Validate();throw new InvalidOperationException("Nested profiles accepted."); } catch(InvalidDataException) { }
            Console.WriteLine("PROFILES PASS: isolated copies, CRUD/order, selection, literal launch arguments, matching, disk roundtrip, nested-data rejection.");
        }
        finally { RuntimeSettings.PersistenceEnabled=persistence;if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }

}
