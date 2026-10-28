using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;

internal static class HostedWindowsLifecycle
{
    internal static string Fixture {get;private set;}="";
    internal static string Output {get;private set;}="";
    internal static bool Allowed=>OperatingSystem.IsWindows()&&
        Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true"&&
        Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")=="github-hosted"&&
        Environment.GetEnvironmentVariable("MAGPIE_HOSTED_WINDOWS_LIFECYCLE")=="1";
    internal static string[] Bootstrap(string[] args)
    {
        if(!Allowed||args.Length!=3||args[0]!="--windows-source-lifecycle-test")
            throw new InvalidOperationException("Windows production lifecycle testing requires a disposable GitHub-hosted Windows runner; no user-desktop fallback.");
        Fixture=Path.GetFullPath(args[1]);Output=Path.GetFullPath(args[2]);
        string expected=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"artifacts","native","win-x64","Release","Release","windows_capture_fixture.exe"));
        if(!string.Equals(Fixture,expected,StringComparison.OrdinalIgnoreCase)||!File.Exists(Fixture))throw new InvalidOperationException("Expected the repository's built source fixture.");
        Directory.CreateDirectory(Output);
        return ["--preview","HostedWindowsSourceLifecycleOutput","--capture",Path.Combine(Output,"owner.png"),"--preview-language","en-US"];
    }
    internal static void Require()
    {
        if(!Allowed||RuntimeSettings.PersistenceEnabled||Fixture.Length==0||Output.Length==0)
            throw new InvalidOperationException("Hosted production capture test was not bootstrapped; refusing GUI/source operations.");
    }
}
internal sealed class HostedLifecycleCase
{
    public string Backend {get;set;}="";
    public bool Fullscreen {get;set;}
    public string State {get;set;}="running";
    public string Phase {get;set;}="create source";
    public string? Error {get;set;}
    public string Device {get;set;}="";
    public uint SourcePid {get;set;}
    public ulong SourceWindow {get;set;}
    public ulong FirstSequence {get;set;}
    public ulong RestoredSequence {get;set;}
    public ulong ShownSequence {get;set;}
    public uint SourceWidth {get;set;}
    public uint SourceHeight {get;set;}
    public bool NativeOutputHidden {get;set;}
    public bool ForegroundPreserved {get;set;}
    public bool InitialForegroundPreserved {get;set;}
    public int NativeKeyboardChecks {get;set;}
    public bool IdleGpuPreserved {get;set;}
    public bool SourceClosedWithoutError {get;set;}
    public bool SourceExitedCleanly {get;set;}
    public bool StopWhileHidden {get;set;}
    public string Screenshot {get;set;}="";
}
internal sealed class HostedLifecycleReport
{
    public bool Passed {get;set;}
    public bool DisposableHostedRunner {get;set;}=true;
    public bool ProductionCaptureLoop {get;set;}=true;
    public bool VisibilityInjected {get;set;}=false;
    public bool SyntheticWin32Source {get;set;}=true;
    public double MinimumFrameRate {get;set;}=30;
    public bool SoftwareVulkan {get;set;}
    public bool HardwareVulkanTested {get;set;}=false;
    public bool NativeInputDeliveryTested {get;set;}=false;
    public bool FullInteractionParity {get;set;}=false;
    public List<HostedLifecycleCase> Cases {get;set;}=[];
}
[JsonSerializable(typeof(HostedLifecycleReport))]
internal partial class HostedLifecycleJson:JsonSerializerContext { }
