using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifySourceLifecycleAsync(MainWindow owner,bool fullscreen)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Source lifecycle fixtures require isolated preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-source-lifecycle-"+Guid.NewGuid().ToString("N"));
        using var engine=NativeCore.CreateForDevice("");if(engine.IsInvalid)throw NativeCore.Error();
        using var capture=NativeCore.CaptureCreate();if(capture.IsInvalid)throw NativeCore.Error();
        try
        {
            _capture=capture;
            if(NativeCore.CaptureSourceState(capture)!=0)throw new InvalidOperationException("Unselected native capture fabricated source availability.");
            _settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="lifecycle";
            if(fullscreen)SuspendSourceOutput(); // suspended before the very first source frame
            OpenOutput(owner,fullscreen,3,2);await Task.Delay(180);
            var output=_output!;var expectedState=fullscreen?WindowState.FullScreen:WindowState.Normal;
            await WaitForWindowStateAsync(()=>output.WindowState==expectedState,"initial source output state");
            byte[] first=[1,2,3,255,5,7,11,255,13,17,19,255,23,29,31,255,37,41,43,255,47,53,59,255];
            var source=new CaptureSource("source","fixture","",11,101,1);
            var processor=new ScalingFrameProcessor(0);NativeEffectStep[] plan=[new(){Effect=0,Width=3,Height=2}];
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:initial");
            if(!ReplayFrame(processor,engine,first,plan,true,0)||!PresentCapturedFrame(processor.Pixels,3,2,source,10))throw new InvalidOperationException("Initial source frame failed.");
            SetDisplayedStages(engine,plan);SetToolbarState(ToolbarState.AlwaysShow);
            if(_sourceSuspended||output.WindowState!=expectedState)throw new InvalidOperationException("First matching frame lost the requested initial mode after waiting.");
            await Task.Delay(200);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:initial");
            if(!fullscreen) {output.Width+=96;output.Height+=64;await Task.Delay(180);}
            var oldSize=output.ClientSize;
            var screenshot=output.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ScreenshotOutputButton");
            var menu=(StageExportFlyout)screenshot.ContextFlyout!;
            menu.ShowAt(screenshot);await Task.Delay(100);
            if(menu.Presenter is not {} presenter||TopLevel.GetTopLevel(presenter) is not PopupRoot {IsVisible:true})
                throw new InvalidOperationException("Source lifecycle export menu is not a real native popup.");
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:hide1");
            RequestScreenshot();SuspendSourceOutput();SuspendSourceOutput();
            await Task.Delay(180);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:hide1");
            if(_stageMenuOpen || menu.Presenter is {} remaining && TopLevel.GetTopLevel(remaining) is PopupRoot {IsVisible:true})
                throw new InvalidOperationException("Suspension left the native export menu visible.");
            if(output.IsVisible||_stop.IsCancellationRequested||!_sourceSuspended||Volatile.Read(ref _screenshotPending)!=0||_stageEngine is not null||_displayedStages.Length!=0)
                throw new InvalidOperationException("Suspending closed the session, retained exports or left output visible.");
            RequestScreenshot();
            if(SaveScreenshot() is not null||await ExportStageAsync(0,0) is not null||Volatile.Read(ref _screenshotPending)!=0||Directory.Exists(folder))
                throw new InvalidOperationException("Suspension exported a stale image or queued screenshot.");
            byte[] restored=(byte[])first.Clone();restored[0]=211;
            if(PresentCapturedFrame(restored,3,2,source,10)||CommitUnchangedFrameSource(source,10)||
                PresentCapturedFrame(restored,3,2,source with {WindowId=202},11)||
                CommitUnchangedFrameSource(source with {ProcessId=99},11)||output.IsVisible||_lastSourceSequence!=10)
                throw new InvalidOperationException("Old sequence or mismatched identity resumed a suspended source.");
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:resume1");
            if(!ReplayFrame(processor,engine,restored,plan,true,1)||!PresentCapturedFrame(processor.Pixels,3,2,source,11))
                throw new InvalidOperationException("Fresh matching frame did not resume output.");
            SetDisplayedStages(engine,plan);
            await Task.Delay(200);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:resume1");
            await WaitForWindowStateAsync(()=>output.IsVisible&&output.WindowState==expectedState,"restored source output state");
            if(_sourceSuspended||_stop.IsCancellationRequested||_lastSourceSequence!=11||!output.ShowActivated)
                throw new InvalidOperationException("Resume did not preserve session and activation preference.");
            if(!fullscreen && (Math.Abs(output.ClientSize.Width-oldSize.Width)>1||Math.Abs(output.ClientSize.Height-oldSize.Height)>1))
                throw new InvalidOperationException("Windowed source recovery lost the output size.");
            string path=SaveScreenshot()??throw new InvalidOperationException("Resumed screenshot unavailable.");
            VerifyPng(path,restored,new PixelSize(3,2));
            // A fresh capture may have identical pixels; identity-only commit
            // must also recover without adding GPU work.
            long rendered=processor.Performance.TotalScaled;
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:hide2");
            SuspendSourceOutput();await Task.Delay(180);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:hide2");
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:resume2");
            if(!CommitUnchangedFrameSource(source,12)||!output.IsVisible||processor.Performance.TotalScaled!=rendered)
                throw new InvalidOperationException("Identical fresh source could not resume without GPU work.");
            await Task.Delay(200);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:resume2");
            var args=Environment.GetCommandLineArgs();int captureIndex=Array.IndexOf(args,"--capture");
            if(captureIndex>=0&&captureIndex+1<args.Length)
            {
                using var bitmap=new RenderTargetBitmap(PixelSize.FromSize(output.ClientSize,1));bitmap.Render((Control)output.Content!);
                bitmap.Save(Path.ChangeExtension(args[captureIndex+1],"output.png"),PngBitmapEncoderOptions.Default);
            }
            Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_BEGIN:hide3");
            SuspendSourceOutput();Stop();await Task.Delay(180);Console.Error.WriteLine("MAGPIE_SOURCE_PHASE_END:hide3");
            if(PresentCapturedFrame(first,3,2,source,13)||CommitUnchangedFrameSource(source,13)||output.IsVisible||_lastSourceSequence!=12)
                throw new InvalidOperationException("Stopped hidden output was resurrected by a late frame.");
            Console.WriteLine("SOURCE LIFECYCLE UI PASS: actual hide/show, session survival, matching fresh/duplicate frame gates, stale screenshots/exports blocked, retained window/fullscreen state, Vulkan/PNG and stop-while-hidden rejection; source visibility/identity are fixture data, no real capture or input.");
        }
        finally
        {
            _capture=null;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
