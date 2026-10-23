using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task<Control> VerifyStageExportAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Stage export fixtures require preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-stage-export-"+Guid.NewGuid().ToString("N"));
        using var engine=NativeCore.Create();if(engine.IsInvalid)throw NativeCore.Error();
        var menu=(MenuFlyout?)null;
        try
        {
            _settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="%WT%_%PN%";
            OpenOutput(owner,false,3,2);await Task.Delay(60);SetToolbarState(ToolbarState.AlwaysShow);
            byte[] input=[1,3,5,255,11,17,23,255,31,41,47,255,53,61,71,255,79,89,97,255,101,107,113,255];
            NativeEffectStep[] plan=[new(){Effect=0,Width=6,Height=4},new(){Effect=3,Width=4,Height=3,Parameter1=0,Parameter2=.5f}];
            var processor=new ScalingFrameProcessor(0);
            if(!ReplayFrame(processor,engine,input,plan,true,0))throw new InvalidOperationException("Stage fixture GPU execution failed.");
            var source=new CaptureSource("阶段窗口","stage-app","",42,101,1);
            if(!PresentCapturedFrame(processor.Pixels,4,3,source,7))throw new InvalidOperationException("Stage fixture presentation failed.");
            SetDisplayedStages(engine,plan);var native=StageSnapshot.Read(engine);
            var button=_output!.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ScreenshotOutputButton");
            menu=(MenuFlyout)button.ContextFlyout!;menu.ShowAt(button);await Task.Delay(60);
            if(menu.Items.OfType<MenuItem>().Count(i=>i.Tag is int)!=2||_toolbar!.Opacity!=1)throw new InvalidOperationException("Context menu stage list/auto-hide protection is missing.");
            if(TopLevel.GetTopLevel(((StageExportFlyout)menu).Presenter!) is not Avalonia.Controls.Primitives.PopupRoot)throw new InvalidOperationException("Output context menu is not a real native popup.");
            var item=menu.Items.OfType<MenuItem>().Single(i=>i.Tag is int index&&index==0);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for(int i=0;i<100&&_stageExportBusy;i++)await Task.Delay(10);
            if(_stageExportBusy||!Directory.Exists(folder)||Directory.GetFiles(folder,"*.png").Length!=1)throw new InvalidOperationException("Actual stage menu command did not export.");
            string first=Directory.GetFiles(folder,"*.png").Single();VerifyPng(first,native[0].Copy(engine),new PixelSize(6,4));
            if(!Path.GetFileName(first).StartsWith("阶段窗口_stage-app_stage1_",StringComparison.Ordinal))throw new InvalidOperationException("Export lost submitted source identity/stage suffix.");
            menu.Hide();
            // Simulate a worker currently committing a newer source. Export must
            // await the same gate and use the newly committed identity, not the
            // identity captured before that transaction completed.
            await _stageGate.WaitAsync();
            var pending=ExportStageAsync(1,3);await Task.Delay(20);
            if(pending.IsCompleted)throw new InvalidOperationException("Export read a stage while rendering owned the gate.");
            CommitPresentedSource(source with {Title="换源后"},8);_stageGate.Release();
            string second=await pending??throw new InvalidOperationException("Export did not resume after frame commit.");
            if(!Path.GetFileName(second).StartsWith("换源后_stage-app_stage2_",StringComparison.Ordinal))throw new InvalidOperationException("Export used stale identity after waiting for render commit.");
            VerifyPng(second,processor.Pixels,new PixelSize(4,3));
            if(processor.Performance.TotalScaled!=1)throw new InvalidOperationException("Stage export inflated render counts.");
            int count=Directory.GetFiles(folder,"*.png").Length;
            if(await ExportStageAsync(0,13) is not null||Directory.GetFiles(folder,"*.png").Length!=count)throw new InvalidOperationException("A stale menu effect exported a different graph.");
            string blocked=Path.Combine(folder,"blocked");File.WriteAllText(blocked,"keep");_settings.ScreenshotDirectory=blocked;
            if(await ExportStageAsync(0,0) is not null||File.ReadAllText(blocked)!="keep"||_stageExportBusy)throw new InvalidOperationException("Write rejection did not recover.");
            _settings.ScreenshotDirectory=folder;
            if(await ExportStageAsync(0,0) is null)throw new InvalidOperationException("Export could not retry after rejection.");
            Stop();
            if(await ExportStageAsync(0,0) is not null||_stageExportBusy)throw new InvalidOperationException("Stopped session accepted export.");
            _finished=true;_output!.Close();_output=null;_bitmap?.Dispose();_bitmap=null;
            // A disposed output's popup host cannot be reparented to another
            // top-level. Generate the same menu for the private recorder owner.
            var review=new StageExportFlyout();RefreshStageMenu(review);
            Controls.PopupAlignment.Track(owner.FindControl<Border>("WindowBorder")!,review);
            review.ShowAt(owner.FindControl<Border>("WindowBorder")!);await Task.Delay(80);
            Console.WriteLine("STAGE EXPORT UI PASS: actual context menu command, real Vulkan stage pixels/PNG dimensions and identity, worker gate/identity handoff, stale effect rejection, write failure/retry and stop; menu regenerated on recorder owner only for isolated composition evidence.");
            if(review.Presenter is not {} presenter||TopLevel.GetTopLevel(presenter) is not Avalonia.Controls.Primitives.PopupRoot)
                throw new InvalidOperationException("Export preview presenter has no native popup root.");
            return presenter;
        }
        finally
        {
            _stageEngine=null;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
