using Avalonia.Threading;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    // Called by the frame worker; no portal await is allowed on its critical path.
    private void UpdateKeepAwake(bool enabled)
    {
        if(_powerDesired!=enabled) { _powerDesired=enabled;_powerUpdateStop?.Cancel(); }
        if(!_powerUpdate.IsCompleted)return;
        if(enabled&&_awake.Active || !enabled&&!_awake.Active&&_awake.Warning is null)return;
        _powerUpdateStop?.Dispose();_powerUpdateStop=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _powerUpdate=ApplyKeepAwakeAsync(enabled,_powerUpdateStop.Token);
    }
    private async Task ApplyKeepAwakeAsync(bool enabled,CancellationToken stop)
    {
        try { await _awake.UpdateAsync(enabled,stop); }
        catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception error) { ReportAwake(error.Message); }
    }
    private void ReportAwake(string? warning)
    {
        if(warning is not null)Status?.Invoke("Keep screen on: "+warning);
        Dispatcher.UIThread.Post(()=>
        {
            if(_output is null||_powerStatus is null)return;
            _powerStatus.Text=warning is null?"":Localization.CurrentLanguage.StartsWith("zh",StringComparison.Ordinal)
                ?"无法保持屏幕常亮，缩放仍在继续。":"Keeping the screen awake is unavailable. Scaling will continue.";
            _powerStatus.IsVisible=warning is not null;
        });
    }
}
