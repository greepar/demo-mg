using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifySourceTitleAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Title fixtures require isolated preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-source-title-"+Guid.NewGuid().ToString("N"));
        using var capture=NativeCore.CaptureCreate();if(capture.IsInvalid)throw NativeCore.Error();
        try
        {
            _capture=capture;_settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="%WT%_%PN%";
            OpenOutput(owner,false,3,2);await Task.Delay(80);
            byte[] pixels=[1,2,3,255,5,7,11,255,13,17,19,255,23,29,31,255,37,41,43,255,47,53,59,255];
            if(!Present(pixels,3,2))throw new InvalidOperationException("Title fixture bitmap failed.");
            var submitted=new CaptureSource("Selected snapshot","source-app","/apps/source-app",42,101,1);CommitPresentedSource(submitted,17);
            // Actual unselected native object must not answer a fabricated owner.
            if(NativeCore.CaptureSnapshotTitle(capture,101,42,17,out _,5400)!=0)throw new InvalidOperationException("Native title query accepted an unselected window identity.");
            int calls=0;CaptureSource? reply=submitted with { Title="Renamed / 😀",ProcessName="wrong-process",ExecutablePath="/wrong/path" };bool fail=false;
            _previewTitleQuery=(frame,sequence)=>
            {
                calls++;
                if(frame!=submitted||sequence!=17)throw new InvalidOperationException("Title query did not receive the exact submitted identity and sequence.");
                if(fail)throw new IOException("fixture title query unavailable");
                return reply;
            };
            string SaveExpected(CaptureSource source)
            {
                string path=SaveScreenshot() ?? throw new InvalidOperationException("Optional title failure prevented screenshot saving.");
                string expected=ScreenshotNames.Format("%WT%_%PN%",source.ScreenshotTitle,source.ScreenshotProcess,DateTime.Now);
                string stem=Path.GetFileNameWithoutExtension(expected),actual=Path.GetFileNameWithoutExtension(path);
                bool collision=actual.StartsWith(stem+"_",StringComparison.Ordinal)&&int.TryParse(actual.AsSpan(stem.Length+1),out int index)&&index>0;
                if(actual!=stem&&!collision)throw new InvalidOperationException("Screenshot used the wrong dynamic title/process: "+Path.GetFileName(path));
                VerifyPng(path,pixels,new PixelSize(3,2));return path;
            }
            string renamed=SaveExpected(submitted with {Title="Renamed / 😀"});
            if(Path.GetFileName(renamed)!="Renamed _ 😀_source-app.png")throw new InvalidOperationException("Dynamic Unicode title was not sanitized or replaced the submitted process identity.");
            reply=submitted with {Title=""};SaveExpected(submitted with {Title=""});
            reply=null;SaveExpected(submitted);
            reply=submitted with {Title="foreign owner",WindowId=202};SaveExpected(submitted);
            reply=submitted with {Title="reused window",ProcessId=99};SaveExpected(submitted);
            reply=submitted with {Title="display",Kind=2};SaveExpected(submitted);
            fail=true;SaveExpected(submitted);fail=false;
            if(calls!=7||ReadSourceInfo()!=submitted||_lastSourceSequence!=17)throw new InvalidOperationException("Optional title refresh mutated cached frame identity or lost its query cases.");
            CommitPresentedSource(CaptureSource.Unknown);SaveExpected(CaptureSource.Unknown);
            if(calls!=7)throw new InvalidOperationException("Unknown source queried a live window title.");
            _previewTitleQuery=null;CommitPresentedSource(submitted,17);SaveExpected(submitted);
            Console.WriteLine("SOURCE TITLE PASS: actual unselected native rejection, submitted window/PID/sequence query, Unicode/empty/live title PNG names, process/path preservation, foreign/reused identity rejection, unavailable/error fallback and unchanged pixel/frame snapshot; live title provider is fixture data, no picker.");
        }
        finally
        {
            _previewTitleQuery=null;_capture=null;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
