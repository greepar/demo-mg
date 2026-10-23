using System.Text.Json;
using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;

internal sealed class RuntimeSettings
{
    public JsonElement? OriginalConfiguration { get; set; }
    public List<ApplicationProfile> Profiles { get; set; } = [];
    public bool IsCroppingEnabled { get; set; }
    public float CroppingLeft { get; set; }
    public float CroppingTop { get; set; }
    public float CroppingRight { get; set; }
    public float CroppingBottom { get; set; }
    internal void ValidateCropping()
    {
        foreach(float edge in new[]{CroppingLeft,CroppingTop,CroppingRight,CroppingBottom})
            if(!float.IsFinite(edge) || edge<0 || edge>16384)throw new InvalidDataException("Invalid crop margins.");
    }
    public int InitialWindowedScaleFactor { get; set; }
    public float CustomInitialWindowedScaleFactor { get; set; }=1.25f;
    public int OutputAlignment { get; set; }=4;
    public CaptureBackend CaptureBackend { get; set; }
    public ToolbarState FullscreenInitialToolbarState { get; set; }=ToolbarState.AutoHide;
    public ToolbarState WindowedInitialToolbarState { get; set; }=ToolbarState.AutoHide;
    public bool GpuProfilingEnabled { get; set; }
    public bool DisableFP16 { get; set; }
    public bool KeepScreenOn { get; set; }
    public DuplicateFrameMode DuplicateFrameDetection { get; set; } = DuplicateFrameMode.Dynamic;
    public bool StatisticsForDynamicDetection { get; set; }
    public double MinimumFrameRate { get; set; }
    public GlobalShortcutBinding FullscreenShortcut { get; set; }=new();
    public GlobalShortcutBinding WindowedShortcut { get; set; }=new() { Key="Q" };
    public GlobalShortcutBinding ToolbarShortcut { get; set; }=new() { Key="D" };
    public GlobalShortcutBinding ScreenshotShortcut { get; set; }=new() { Key="S" };
    public bool ShowTrayIcon { get; set; }=true;
    public int CountdownSeconds { get; set; }=3;
    public string Language { get; set; } = "";
    public int Theme { get; set; }
    public string GraphicsDeviceId { get; set; } = "";
    public string? ActiveProfileId { get; set; }
    [JsonIgnore]
    public RuntimeSettings ActiveSettings => ActiveProfileId is { } id ? Profiles.Single(p=>p.Id==id).Settings : this;
    public RuntimeSettings CopyForProfile()
    {
        var copy=(RuntimeSettings)MemberwiseClone();
        copy.FullscreenShortcut=new() { Key=FullscreenShortcut.Key,Modifiers=FullscreenShortcut.Modifiers };
        copy.WindowedShortcut=new() { Key=WindowedShortcut.Key,Modifiers=WindowedShortcut.Modifiers };
        copy.ToolbarShortcut=new() { Key=ToolbarShortcut.Key,Modifiers=ToolbarShortcut.Modifiers };
        copy.ScreenshotShortcut=new() { Key=ScreenshotShortcut.Key,Modifiers=ScreenshotShortcut.Modifiers };
        copy.Crt=(float[])Crt.Clone();
        copy.GraphParameters=new(GraphParameters,StringComparer.Ordinal);
        copy.Profiles=[];copy.ActiveProfileId=null;copy.OriginalConfiguration=null;
        // Effect modes are shared definitions; per-application values and selection are independent.
        copy.Modes=Modes;
        return copy;
    }
    public ApplicationProfile AddProfile(string name,string executablePath,string? copyFromId=null)
    {
        if(!Path.IsPathFullyQualified(executablePath))throw new InvalidDataException("Choose an absolute executable path.");
        var sourceProfile=copyFromId is null ? null : Profiles.Single(p=>p.Id==copyFromId);
        var source=sourceProfile?.Settings ?? this;
        var profile=new ApplicationProfile { Name=name.Trim(),ExecutablePath=Path.GetFullPath(executablePath),Settings=source.CopyForProfile(),Arguments=sourceProfile?.Arguments.ToList() ?? [] };
        Profiles.Add(profile);
        try { Save(); } catch { Profiles.Remove(profile);throw; }
        return profile;
    }
    public void RenameProfile(string id,string name)
    {
        var profile=Profiles.Single(p=>p.Id==id);string previous=profile.Name;profile.Name=name.Trim();
        try { Save(); } catch { profile.Name=previous;throw; }
    }
    public void MoveProfile(string id,int direction)
    {
        if(direction is not (-1 or 1))throw new ArgumentOutOfRangeException(nameof(direction));
        int index=Profiles.FindIndex(p=>p.Id==id),target=index+direction;
        if(index<0)throw new InvalidDataException("Profile does not exist.");
        if(target<0 || target>=Profiles.Count)return;
        (Profiles[index],Profiles[target])=(Profiles[target],Profiles[index]);
        try { Save(); } catch { (Profiles[index],Profiles[target])=(Profiles[target],Profiles[index]);throw; }
    }
    public void DeleteProfile(string id)
    {
        int index=Profiles.FindIndex(p=>p.Id==id);
        if(index<0)throw new InvalidDataException("Profile does not exist.");
        var profile=Profiles[index];string? previous=ActiveProfileId;
        Profiles.RemoveAt(index);if(ActiveProfileId==id)ActiveProfileId=null;
        try { Save(); } catch { Profiles.Insert(index,profile);ActiveProfileId=previous;throw; }
    }
    public void UseProfile(string? id)
    {
        string? previous=ActiveProfileId;ActiveProfileId=id;
        try { Save(); } catch { ActiveProfileId=previous;throw; }
    }
    public float[] Crt { get; set; } = CrtParameters.Defaults();
    public List<EffectModeSettings> Modes { get; set; } = [];
    public string? SelectedModeId { get; set; }
    public NativeEffectStep[] ResolveChain(uint width, uint height, uint viewportWidth, uint viewportHeight,bool windowed=false)
    {
        ValidateOptions();
        if (SelectedModeId is { } id) return Modes.Single(m => m.Id == id).Resolve(width, height, viewportWidth, viewportHeight,windowed);
        if(GraphEffects.Contains(Effect))return [GraphEffects.Stage(this).Resolve(width,height,viewportWidth,viewportHeight,windowed)];
        if(EffectStageSettings.IsFixed2x(Effect)||Effect is >=20 and <=23)return [new EffectStageSettings { Effect=Effect }.Resolve(width,height,viewportWidth,viewportHeight,windowed)];
        if(Effect==12) { var stage=new EffectStageSettings { Effect=12,Crt=Crt,ScaleType=ScaleType,ScaleX=ScaleX,ScaleY=ScaleY,Width=AbsoluteWidth,Height=AbsoluteHeight };return [stage.Resolve(width,height,viewportWidth,viewportHeight,windowed)]; }
        if (Effect == 5) return [new() { Effect = 5, Width = width, Height = height, Parameter1 = FsrSharpness }];
        var size = OutputSize(width, height, viewportWidth, viewportHeight,windowed);
        if (Effect == 6) return [new() { Effect = 4, Width = size.Width, Height = size.Height }, new() { Effect = 5, Width = size.Width, Height = size.Height, Parameter1 = FsrSharpness }];
        return [new() { Effect = Effect, Width = size.Width, Height = size.Height, Parameter1 = Effect==14 ? SsimOversharp : Effect == 3 ? BicubicB : Effect == 5 ? FsrSharpness : LanczosAntiRinging, Parameter2 = BicubicC }];
    }
    [JsonIgnore]
    public string SelectedModeName => SelectedModeId is { } id ? Modes.FirstOrDefault(m => m.Id == id)?.Name ?? "" : EffectStageSettings.Name(Effect);
    public void EnsureSelectedModeAvailable()
    {
        if(SelectedModeId is {} id)Modes.Single(m=>m.Id==id).EnsureAvailable();
    }
    private bool _frameRateLimiterEnabled;
    private double _maximumFrameRate=60;
    public bool FrameRateLimiterEnabled { get => Volatile.Read(ref _frameRateLimiterEnabled);set => Volatile.Write(ref _frameRateLimiterEnabled,value); }
    public double MaximumFrameRate { get => Volatile.Read(ref _maximumFrameRate);set => Volatile.Write(ref _maximumFrameRate,value); }
    [JsonIgnore]
    public double EffectiveMaximumFrameRate => FrameRateLimiterEnabled ? MaximumFrameRate : 0;
    public string ScreenshotDirectory { get; set; } = "";
    public string ScreenshotTemplate { get; set; } = "";
    public string ResolveScreenshotDirectory()
    {
        if (!string.IsNullOrEmpty(ScreenshotDirectory)) return ScreenshotDirectory;
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(pictures)) pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        return Path.Combine(pictures, "Magpie");
    }
    public string ResolveScreenshotTemplate() => string.IsNullOrEmpty(ScreenshotTemplate) ? ScreenshotNames.DefaultTemplate : ScreenshotTemplate;
    public float SsimOversharp { get; set; }=1;
    public float FsrSharpness { get; set; } = .87f;
    public Dictionary<string,float> GraphParameters { get; set; } = new(StringComparer.Ordinal);
    public uint Effect { get; set; } = 1;
    public float BicubicC { get; set; } = .33f;
    public float BicubicB { get; set; } = .33f;
    public int Version { get; set; } = 1;
    public float LanczosAntiRinging { get; set; } = .5f;
    public int ScaleType { get; set; } = 1;
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public int AbsoluteWidth { get; set; } = 1920;
    public int AbsoluteHeight { get; set; } = 1080;
    private static long _changeSequence;
    public static long ChangeSequence=>Interlocked.Read(ref _changeSequence);
    public static RuntimeSettings Current { get; private set; } = new();
    public static bool PersistenceEnabled { get; set; } = true;
    internal static ConfigurationLocation Location { get; }=ConfigurationLocation.Create();
    public static string FilePath=>Location.Path;
    public static void Load()
    {
        if (!PersistenceEnabled || !File.Exists(FilePath)) return;
        Current=ReadFile(FilePath);
    }
    internal static RuntimeSettings ReadFile(string path)
    {
        var settings=JsonSerializer.Deserialize(File.ReadAllText(path),SettingsJson.Default.RuntimeSettings)??throw new InvalidDataException("Empty settings file.");
        ScalingModeInterchange.PromoteAvailable(settings);settings.Validate();return settings;
    }
    public void Validate()
    {
        if(OriginalConfiguration is {} original)global::Magpie.Desktop.Core.OriginalConfiguration.ValidatePreserved(original);
        if(Profiles is null || Profiles.Count>100 || Profiles.Any(p=>p is null))throw new InvalidDataException("Invalid profiles list.");
        foreach(var profile in Profiles)
        {
            if(profile.Settings is not null)profile.Settings.Modes=Modes;
            profile.Validate();
        }
        if(Profiles.Select(p=>p.Id).Distinct().Count()!=Profiles.Count || Profiles.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Profiles.Count ||
            (ActiveProfileId is not null && Profiles.All(p=>p.Id!=ActiveProfileId)))throw new InvalidDataException("Duplicate or missing application profile.");
        ValidateOptions();
    }
    private void ValidateOptions()
    {
        GlobalShortcutSettings.Validate(this);
        if(CountdownSeconds is <1 or >5)throw new InvalidDataException("Invalid countdown duration.");
        ValidateCropping();
        if(InitialWindowedScaleFactor is <0 or >6 || !float.IsFinite(CustomInitialWindowedScaleFactor) || CustomInitialWindowedScaleFactor is <1 or >10 || OutputAlignment is <0 or >8)throw new InvalidDataException("Invalid window scale or output alignment.");
        if(!Enum.IsDefined(DuplicateFrameDetection) || !double.IsFinite(MinimumFrameRate) || MinimumFrameRate is <0 or >1000)throw new InvalidDataException("Invalid frame detection or minimum frame rate.");
        _=Localization.NormalizePreference(Language);
        _=ThemePreference.Variant(Theme);
        if(!Enum.IsDefined(CaptureBackend))throw new InvalidDataException("Invalid capture backend.");
        if(!Enum.IsDefined(FullscreenInitialToolbarState)||!Enum.IsDefined(WindowedInitialToolbarState))throw new InvalidDataException("Invalid initial toolbar state.");
        VulkanDevices.ValidateIdentifier(GraphicsDeviceId);
        CrtParameters.Validate(Crt);
        if (!double.IsFinite(MaximumFrameRate) || MaximumFrameRate is <10 or >1000)
            throw new InvalidDataException("Maximum frame rate must be between 10 and 1000 FPS.");
        if (ScreenshotDirectory is null || ScreenshotDirectory.Length > 4096 || ScreenshotDirectory.Contains('\0') ||
            (ScreenshotDirectory.Length > 0 && !Path.IsPathFullyQualified(ScreenshotDirectory)))
            throw new InvalidDataException("Screenshot directory must be an absolute local path.");
        if (ScreenshotTemplate is null || ScreenshotTemplate.Length > 1024 || !ScreenshotNames.IsValidTemplate(ScreenshotTemplate))
            throw new InvalidDataException("Invalid screenshot filename template.");
        if (Modes is null || Modes.Count > 100 || Modes.Any(m => m is null)) throw new InvalidDataException("Invalid scaling modes list.");
        foreach (var mode in Modes) mode.Validate();
        if (Modes.Select(m => m.Id).Distinct().Count() != Modes.Count || Modes.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Modes.Count)
            throw new InvalidDataException("Duplicate scaling mode id or name.");
        if (SelectedModeId is not null && Modes.All(m => m.Id != SelectedModeId)) throw new InvalidDataException("Selected scaling mode does not exist.");
        GraphEffects.Validate(this);
        if(!float.IsFinite(SsimOversharp) || SsimOversharp is <1 or >3)throw new InvalidDataException("SSim oversharp must be between 1 and 3.");
        if (!float.IsFinite(FsrSharpness) || FsrSharpness is < 0 or > 1 || !float.IsFinite(BicubicC) || BicubicC is < 0 or > 1 || (!GraphCatalog.IsRegistered(Effect)&&Effect!=6) || !float.IsFinite(BicubicB) || BicubicB is < 0 or > 1 || Version != 1 || !float.IsFinite(LanczosAntiRinging) || LanczosAntiRinging is < 0 or > 1 || ScaleType is < 0 or > 3 ||
            !double.IsFinite(ScaleX) || !double.IsFinite(ScaleY) || ScaleX is < 1e-20 or > 1e20 || ScaleY is < 1e-20 or > 1e20 ||
            AbsoluteWidth is < 1 or > 16384 || AbsoluteHeight is < 1 or > 16384) throw new InvalidDataException("Invalid scaling settings.");
    }
    public void Save()
    {
        Validate();
        if (PersistenceEnabled)WriteFile(FilePath);
        Interlocked.Increment(ref _changeSequence);
    }
    internal void WriteFile(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, SettingsJson.Default.RuntimeSettings));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public (uint Width, uint Height) OutputSize(uint width, uint height, uint viewportWidth, uint viewportHeight,bool windowed=false)
    {
        ValidateOptions();
        double x, y;
        if (ScaleType == 0) { x = width * (float)ScaleX; y = height * (float)ScaleY; }
        else if (ScaleType == 1 && !(windowed && PresentationPlan.UnitScale(ScaleX,ScaleY))) { float fit = MathF.Min((float)viewportWidth / width, (float)viewportHeight / height); x = width * fit * (float)ScaleX; y = height * fit * (float)ScaleY; }
        else if (ScaleType == 2) { x = AbsoluteWidth; y = AbsoluteHeight; }
        else { x = viewportWidth; y = viewportHeight; }
        x=Math.Round(x,MidpointRounding.AwayFromZero);y=Math.Round(y,MidpointRounding.AwayFromZero);
        if (x < 1 || y < 1 || x > 16384 || y > 16384 || x * y > 16777216) throw new InvalidOperationException("Requested scaling output exceeds the 16-megapixel limit.");
        return ((uint)x, (uint)y);
    }
}
[JsonSerializable(typeof(RuntimeSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJson : JsonSerializerContext { }
