using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifySourceFrameIdentityAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Source frame fixtures require isolated preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-source-frame-"+Guid.NewGuid().ToString("N"));
        using var capture=NativeCore.CaptureCreate();if(capture.IsInvalid)throw NativeCore.Error();
        using var engine=NativeCore.CreateForDevice("");if(engine.IsInvalid)throw NativeCore.Error();
        try
        {
            _capture=capture;_settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="%WT%_%PN%";
            OpenOutput(owner,false,3,2);await Task.Delay(80);
            var processor=new ScalingFrameProcessor(0);NativeEffectStep[] plan=[new(){Effect=0,Width=3,Height=2}];
            byte[] first=[1,2,3,255,5,7,11,255,13,17,19,255,23,29,31,255,37,41,43,255,47,53,59,255];
            byte[] second=(byte[])first.Clone();second[0]=211;
            var a=new CaptureSource("first","application-a","",11,101,1);
            var b=new CaptureSource("second","application-b","",22,202,1);
            if(!ReplayFrame(processor,engine,first,plan,true,0)||!Present(processor.Pixels,3,2))throw new InvalidOperationException("Initial source frame failed.");
            CommitPresentedSource(a,1);
            // The actual unselected native capture reports Unknown. A screenshot
            // must use its displayed frame snapshot rather than querying it.
            if(ReadCapturedSource(capture)!=CaptureSource.Unknown)throw new InvalidOperationException("Unselected capture fixture fabricated an identity.");
            if(NativeCore.CaptureFrameSequence(capture)!=0)throw new InvalidOperationException("Unselected native capture fabricated a frame sequence.");
            string original=SaveScreenshot()!;
            if(Path.GetFileName(original)!="first_application-a.png")throw new InvalidOperationException("Screenshot replaced displayed identity with a live native snapshot.");
            VerifyPng(original,first,new PixelSize(3,2));
            if(!ReplayFrame(processor,engine,second,plan,true,1))throw new InvalidOperationException("Replacement source did not process.");
            // New GPU pixels/identity are ready on the worker, but no UI bitmap
            // submission has happened yet. The old screenshot remains coherent.
            string queued=SaveScreenshot()!;
            if(!Path.GetFileName(queued).StartsWith("first_application-a",StringComparison.Ordinal)||ReadSourceInfo()!=a||_lastSourceSequence!=1)throw new InvalidOperationException("Queued worker frame changed the displayed source identity/sequence.");
            VerifyPng(queued,first,new PixelSize(3,2));
            if(!Present(processor.Pixels,3,2))throw new InvalidOperationException("Replacement source bitmap was not presented.");
            CommitPresentedSource(b,2);string replaced=SaveScreenshot()!;
            if(Path.GetFileName(replaced)!="second_application-b.png")throw new InvalidOperationException("Presented replacement kept the previous source identity.");
            VerifyPng(replaced,second,new PixelSize(3,2));
            if(ReplayFrame(processor,engine,second,plan,true,2))throw new InvalidOperationException("Duplicate-source identity change reran Vulkan.");
            var duplicate=b with { Title="same-pixels-new-source",WindowId=303 };
            CommitPresentedSource(duplicate,3);string unchanged=SaveScreenshot()!;
            if(Path.GetFileName(unchanged)!="same-pixels-new-source_application-b.png"||processor.Performance.TotalScaled!=2||_lastSourceSequence!=3)throw new InvalidOperationException("Identical pixels did not accept a new source identity/sequence without GPU work.");
            VerifyPng(unchanged,second,new PixelSize(3,2));
            CommitPresentedSource(CaptureSource.Unknown);string unknown=SaveScreenshot()!;
            if(Path.GetFileName(unknown).Contains("application-b",StringComparison.Ordinal))throw new InvalidOperationException("Unknown replacement inherited the old application's identity.");
            VerifyPng(unknown,second,new PixelSize(3,2));
            UpdateInputStatus(3);
            if(_inputStatus is not { IsVisible:true }||string.IsNullOrWhiteSpace(_inputStatus.Text))throw new InvalidOperationException("Pending source/frame identity did not display its waiting message.");
            SetToolbarState(ToolbarState.AlwaysShow);await Task.Delay(80);
            var args=Environment.GetCommandLineArgs();int captureIndex=Array.IndexOf(args,"--capture");
            if(captureIndex>=0&&captureIndex+1<args.Length)
            {
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSize.FromSize(_output!.ClientSize,1));bitmap.Render((Control)_output.Content!);
                bitmap.Save(Path.ChangeExtension(args[captureIndex+1],"output.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            UpdateInputStatus(1);
            if(_inputStatus.IsVisible)throw new InvalidOperationException("Matching source/frame did not clear the waiting message.");
            Console.WriteLine("SOURCE FRAME IDENTITY PASS: actual unselected native snapshot, real Vulkan/PNG pairs before and after queued UI submission, duplicate source identity without GPU rerun and unknown-source reset; selected identities/pixels are fixture data, no picker.");
        }
        finally
        {
            _capture=null;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
