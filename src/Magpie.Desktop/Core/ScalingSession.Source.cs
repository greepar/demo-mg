namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private CaptureSource _lastSourceInfo=CaptureSource.Unknown;
    private ulong _lastSourceSequence;
    private Func<CaptureSource,ulong,CaptureSource?>? _previewTitleQuery;
    // UI-owned snapshot: screenshot/input identity follows the bitmap that was
    // actually submitted, never a newer frame waiting on the worker thread.
    private CaptureSource ReadSourceInfo()=>_lastSourceInfo;
    private CaptureSource ReadScreenshotSource()
    {
        var frame=ReadSourceInfo();
        if(frame.Kind!=1||frame.WindowId==0||frame.ProcessId==0)return frame;
        try
        {
            CaptureSource? current=null;
            if(_previewTitleQuery is {} query)
            {
                if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Title fixtures require isolated preview mode.");
                current=query(frame,_lastSourceSequence);
            }
            else
            {
                lock(_captureLock)
                    if(_capture is { IsClosed:false,IsInvalid:false } capture && NativeCore.CaptureSnapshotTitle(capture,frame.WindowId,frame.ProcessId,_lastSourceSequence,out var native,5400)==1 && (native.Flags&1)!=0)
                        current=CaptureSource.Decode(native);
            }
            // Refresh only the title. Process/path/identity still belong to the
            // submitted bitmap, even if the provider returned newer metadata.
            if(current is { Kind:1 } && current.WindowId==frame.WindowId && current.ProcessId==frame.ProcessId)
                return frame with { Title=current.Title };
        }
        catch(Exception error) { Console.Error.WriteLine("Source title: "+error.Message); }
        return frame;
    }
    private static CaptureSource ReadCapturedSource(CaptureHandle capture)
    {
        try
        {
            if(NativeCore.CaptureSourceInfo(capture,out var native,5400)!=0)return CaptureSource.Decode(native);
        }
        catch(Exception error) { Console.Error.WriteLine("Source metadata: "+error.Message); }
        return CaptureSource.Unknown;
    }
    private void CommitPresentedSource(CaptureSource source,ulong sequence=0)
    {
        _lastSourceInfo=source;_lastSourceSequence=sequence;ConfigureMacInput();
    }
}
