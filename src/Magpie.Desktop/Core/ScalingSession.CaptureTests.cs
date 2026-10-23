using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static async Task VerifyUnsupportedCaptureAsync(Window owner,RuntimeSettings settings)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Capture fixtures require isolated preview mode.");
        bool entered=false,finished=false;string? error=null;
        var session=new ScalingSession(settings);
        session._previewLoop=_=> { entered=true;throw new InvalidOperationException("Invalid capture backend passed startup validation."); };
        session.Finished+=failure=> { finished=true;error=failure; };
        await session.RunAsync(owner,false,0).WaitAsync(TimeSpan.FromSeconds(8));
        if(entered||!finished||string.IsNullOrWhiteSpace(error)||!error.Contains(CaptureBackendPolicy.Name(settings.CaptureBackend),StringComparison.Ordinal)||session._output is not null||session._capture is not null)
            throw new InvalidOperationException("Unsupported capture did not fail cleanly before native engine/capture creation.");
    }
}
