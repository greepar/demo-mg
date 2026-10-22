using System.Text.Json;
using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;
internal sealed record ScreenAwakeProbeState(string State,bool Active,int Attempts,int Frames,string? Warning,string? Error);
[JsonSerializable(typeof(ScreenAwakeProbeState))]
internal partial class ScreenAwakeProbeJson : JsonSerializerContext { }
internal static class ScreenAwakeProbe
{
    internal static int Run(string receipt)
    {
        if(Environment.GetEnvironmentVariable("MAGPIE_POWER_PRIVATE_SESSION")!="1")throw new InvalidOperationException("Power probes require a private test bus.");
        return RunAsync(receipt).GetAwaiter().GetResult();
    }
    private static async Task<int> RunAsync(string receipt)
    {
        int attempts=0,frames=0;string state="acquiring";string? error=null;
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var power=new ScreenAwakeController(token=> { attempts++;return PortalScreenAwakeLease.AcquireAsync(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!,"Magpie test — 常亮",token); });
        void Write()
        {
            File.WriteAllText(receipt+".tmp",JsonSerializer.Serialize(new ScreenAwakeProbeState(state,power.Active,attempts,frames,power.Warning,error),ScreenAwakeProbeJson.Default.ScreenAwakeProbeState));File.Move(receipt+".tmp",receipt,true);
        }
        try
        {
            var acquisition=power.UpdateAsync(true,stop.Token).AsTask();Write();
            while(!acquisition.IsCompleted)
            {
                if(File.Exists(receipt+".cancel"))stop.Cancel();frames++;Write();await Task.Delay(20);
            }
            await acquisition;
            if(!power.Active) { state="failed";error=power.Warning;await power.DisposeAsync();Write();return 0; }
            while(!File.Exists(receipt+".stop")&&!stop.IsCancellationRequested)
            {
                bool enabled=!File.Exists(receipt+".disable");
                await power.UpdateAsync(enabled,stop.Token);state=power.Active?"active":enabled?"lost":"disabled";frames++;Write();await Task.Delay(20);
            }
            await power.DisposeAsync();await power.DisposeAsync();state="disposed";Write();return 0;
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { await power.DisposeAsync();state="cancelled";Write();return 0; }
        catch(Exception failure) { error=failure.Message;await power.DisposeAsync();state="failed";Write();return 0; }
    }
}
