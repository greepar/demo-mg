using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Magpie.Desktop.Core;
internal sealed record ShortcutProbeState(string State,BoundShortcut[] Bound,string[] Activations,string[] Closed,string? Error);
[JsonSerializable(typeof(ShortcutProbeState))]
internal partial class ShortcutProbeJson : JsonSerializerContext { }
internal static class GlobalShortcutProbe
{
    internal static int Run(string receipt)
    {
        if(Environment.GetEnvironmentVariable("MAGPIE_SHORTCUT_PRIVATE_SESSION")!="1")throw new InvalidOperationException("Shortcut probes require a private test bus.");
        return RunAsync(receipt).GetAwaiter().GetResult();
    }
    private static async Task<int> RunAsync(string receipt)
    {
        string stage="binding";string? error=null;
        var actions=new ConcurrentQueue<string>();var closed=new ConcurrentQueue<string>();
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var portal=new PortalGlobalShortcuts(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!);
        portal.Activated+=actions.Enqueue;portal.Closed+=closed.Enqueue;
        void Write()
        {
            var state=new ShortcutProbeState(stage,portal.Bound.ToArray(),actions.ToArray(),closed.ToArray(),error);
            File.WriteAllText(receipt+".tmp",JsonSerializer.Serialize(state,ShortcutProbeJson.Default.ShortcutProbeState));File.Move(receipt+".tmp",receipt,true);
        }
        var binding=portal.BindAsync(GlobalShortcutSettings.Definitions(new RuntimeSettings()),"",cancel.Token);Write();
        while(!binding.IsCompleted)
        {
            if(File.Exists(receipt+".cancel"))cancel.Cancel();
            await Task.Delay(20);
        }
        try { await binding;stage="bound";Write(); }
        catch(Exception failure)
        {
            error=failure.Message;stage="failed";await portal.DisposeAsync();Write();return 0;
        }
        while(!File.Exists(receipt+".stop")&&!cancel.IsCancellationRequested)
        {
            Write();await Task.Delay(30);
        }
        await portal.DisposeAsync();stage="disposed";Write();return 0;
    }
}
