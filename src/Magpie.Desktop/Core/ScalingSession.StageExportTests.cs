using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task<List<Control>> VerifyStageExportAsync(MainWindow owner,bool developer=false)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Stage export fixtures require preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-stage-export-"+Guid.NewGuid().ToString("N"));
        using var engine=NativeCore.Create();if(engine.IsInvalid)throw NativeCore.Error();
        var menu=(MenuFlyout?)null;
        bool previousDeveloper=RuntimeSettings.Current.DeveloperMode;RuntimeSettings.Current.DeveloperMode=false;
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
            if(developer)await VerifyPassCommandsAsync(owner,engine,processor,folder,source);
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
            var roots=new List<Control>{presenter};
            if(developer)
            {
                var stage=review.Items.OfType<MenuItem>().Single(i=>i.Tag is int index&&index==0);
                stage.IsSubMenuOpen=true;await Task.Delay(100);
                var pass=stage.Items.OfType<MenuItem>().First();
                if(TopLevel.GetTopLevel(pass) is not Avalonia.Controls.Primitives.PopupRoot passRoot)throw new InvalidOperationException("Pass list has no native submenu.");
                roots.Add(pass);
                pass.IsSubMenuOpen=true;await Task.Delay(100);
                var texture=pass.Items.OfType<MenuItem>().First();
                if(TopLevel.GetTopLevel(texture) is not Avalonia.Controls.Primitives.PopupRoot textureRoot)throw new InvalidOperationException("Texture list has no native submenu.");
                roots.Add(texture);
            }
            return roots;
        }
        finally
        {
            RuntimeSettings.Current.DeveloperMode=previousDeveloper;
            _stageEngine=null;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private async Task VerifyPassCommandsAsync(MainWindow owner,EngineHandle engine,ScalingFrameProcessor processor,string folder,CaptureSource source)
    {
        var home=owner.GetVisualDescendants().OfType<Views.HomePage>().Single();
        owner.ShowDeveloperOptions();
        var toggle=home.FindControl<Avalonia.Controls.ToggleSwitch>("DeveloperModeToggle")!;
        if(!RuntimeSettings.Current.DeveloperMode||toggle.IsChecked!=true||!home.FindControl<Controls.SettingsExpander>("DeveloperOptions")!.IsVisible)throw new InvalidOperationException("About activation did not persist developer mode.");
        toggle.IsChecked=false;
        if(RuntimeSettings.Current.DeveloperMode||home.FindControl<Controls.SettingsExpander>("DeveloperOptions")!.IsVisible)throw new InvalidOperationException("Developer toggle did not disable and hide the section.");
        owner.ShowDeveloperOptions();
        byte[] input=Enumerable.Range(0,24).Select(i=>(byte)(i%4==3?255:80)).ToArray();
        NativeEffectStep[] plan=[new(){Effect=8,Width=6,Height=4}];
        if(!ReplayFrame(processor,engine,input,plan,true,0)||!PresentCapturedFrame(processor.Pixels,6,4,source,9))throw new InvalidOperationException("Developer fixture GPU presentation failed.");
        SetDisplayedStages(engine,plan);
        var menu=(MenuFlyout)_output!.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ScreenshotOutputButton").ContextFlyout!;
        var button=_output!.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ScreenshotOutputButton");
        menu.ShowAt(button);await Task.Delay(80);
        var stage=menu.Items.OfType<MenuItem>().Single(i=>i.Tag is int index&&index==0);
        var descriptions=PassExportCatalog.All[8];var passes=stage.Items.OfType<MenuItem>().ToArray();
        if(!passes.Select(i=>(string)i.Header!).SequenceEqual(descriptions.Select(p=>p.Name)))throw new InvalidOperationException("Developer menu pass names/order differ from original.");
        for(int pass=0;pass<passes.Length;pass++)
        {
            var children=passes[pass].Items.OfType<MenuItem>().ToArray();
            if(descriptions[pass].Outputs.Length>1&&!children.Select(i=>(string)i.Header!).SequenceEqual(descriptions[pass].Outputs.Select(o=>o.Name)))throw new InvalidOperationException("Multi-output menu texture names/order differ.");
            for(uint output=0;output<descriptions[pass].Outputs.Length;output++)
            {
                var snapshot=PassSnapshot.Read(engine,0,(uint)pass,output);byte[] expected=snapshot.Copy(engine);
                var before=Directory.GetFiles(folder).ToHashSet(StringComparer.Ordinal);
                var command=children.Length==0?passes[pass]:children[output];
                command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for(int i=0;i<200&&_stageExportBusy;i++)await Task.Delay(10);
                var files=Directory.GetFiles(folder).Where(f=>!before.Contains(f)).ToArray();
                if(_stageExportBusy||files.Length!=1)throw new InvalidOperationException("Developer menu command did not export exactly one file.");
                if(snapshot.Texture==uint.MaxValue)VerifyPng(files[0],expected,new PixelSize(6,4));
                else
                {
                    using var reference=new MemoryStream();DdsExport.Write(reference,snapshot,expected);
                    if(Path.GetExtension(files[0])!=".dds"||!File.ReadAllBytes(files[0]).SequenceEqual(reference.ToArray()))throw new InvalidOperationException("Menu DDS differs from exact native pass data.");
                }
            }
        }
        menu.Hide();
        int count=Directory.GetFiles(folder).Length;
        RuntimeSettings.Current.DeveloperMode=false;
        if(await ExportStageAsync(0,8,0,0) is not null||Directory.GetFiles(folder).Length!=count)throw new InvalidOperationException("Disabled developer mode accepted a stale pass command.");
        RuntimeSettings.Current.DeveloperMode=true;
        string blocked=Path.Combine(folder,"pass-blocked");File.WriteAllText(blocked,"keep");_settings.ScreenshotDirectory=blocked;
        if(await ExportStageAsync(0,8,0,0) is not null||File.ReadAllText(blocked)!="keep"||_stageExportBusy)throw new InvalidOperationException("DDS write failure did not recover.");
        _settings.ScreenshotDirectory=folder;
        if(await ExportStageAsync(0,8,0,0) is null||Directory.GetFiles(folder,"*.tmp").Length!=0)throw new InvalidOperationException("DDS retry failed or left temporary data.");
        Console.WriteLine("DEVELOPER PASS UI PASS: About activation, actual toggle, original pass/texture menu commands, seven exact Vulkan DDS/PNG outputs, disabled-mode stale command guard, write failure/retry and temporary cleanup. Source pixels are injected; submenu clicks use routed events.");
    }
}
