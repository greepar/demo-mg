using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private volatile bool _sourceSuspended;
    private ulong _suspendedSequence;
    private CaptureSource _suspendedSource=CaptureSource.Unknown;
    private bool _suspendedHadOutput;
    private WindowState _suspendedWindowState;
    private Size _suspendedClientSize;
    private PixelPoint _suspendedPosition;

    // UI thread. Preserve the session/source, but remove its stale presentation
    // and export actions until the worker has submitted a new matching frame.
    private void SuspendSourceOutput()
    {
        if(_sourceSuspended||_stop.IsCancellationRequested)return;
        _sourceSuspended=true;_suspendedSequence=_lastSourceSequence;_suspendedSource=_lastSourceInfo;
        _suspendedHadOutput=_output is not null;
        Interlocked.Exchange(ref _screenshotPending,0);
        EndProfilerDrag(false);_stageMenu?.Hide();_graphParameterFlyout?.Hide();
        if(_output is {} output)
        {
            _suspendedWindowState=output.WindowState;_suspendedClientSize=output.ClientSize;_suspendedPosition=output.Position;
            foreach(var combo in output.GetVisualDescendants().OfType<ComboBox>().ToArray())combo.IsDropDownOpen=false;
            output.Hide();
            DetachSuspendedWindowsOwner();
        }
        _stageEngine=null;_displayedStages=[];
        Status?.Invoke(Localization.CurrentLanguage.StartsWith("zh",StringComparison.Ordinal)?
            "源窗口已最小化或隐藏，正在等待恢复。":"The source is minimized or hidden. Waiting for it to return.");
    }
    private bool CanResumeSource(CaptureSource source,ulong sequence)=>!_sourceSuspended ||
        (sequence>_suspendedSequence && (_suspendedSource.WindowId==0 ||
            (source.Kind==1 && source.WindowId==_suspendedSource.WindowId && source.ProcessId==_suspendedSource.ProcessId)));

    private void ResumeSourceOutput()
    {
        if(!_sourceSuspended||_stop.IsCancellationRequested||_output is null)return;
        _sourceSuspended=false;
        // Returning from minimize must not steal focus from the source window.
        bool activated=_output.ShowActivated;
        try
        {
            _output.ShowActivated=false;
            if(_suspendedHadOutput)_output.WindowStartupLocation=WindowStartupLocation.Manual;
            if(_suspendedHadOutput&&_suspendedWindowState==WindowState.Normal && _suspendedClientSize.Width>0 && _suspendedClientSize.Height>0)
            { _output.Width=_suspendedClientSize.Width;_output.Height=_suspendedClientSize.Height; }
            _output.Show();
            if(_suspendedHadOutput)_output.WindowState=_suspendedWindowState;
            if(_suspendedHadOutput&&OperatingSystem.IsWindows()&&_suspendedWindowState==WindowState.Normal)_output.Position=_suspendedPosition;
            if(OperatingSystem.IsWindows())_windowsOutputNeedsLayerRestore=true;
        }
        finally { _output.ShowActivated=activated; }
        UpdateSize();
        Status?.Invoke(Localization.CurrentLanguage.StartsWith("zh",StringComparison.Ordinal)?"源窗口已恢复，继续缩放。":"The source returned. Scaling resumed.");
    }
}
