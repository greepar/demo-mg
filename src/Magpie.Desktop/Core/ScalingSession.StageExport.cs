using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace Magpie.Desktop.Core;

internal sealed class StageExportFlyout : MenuFlyout
{
    internal Control? Presenter=>Popup.Child;
}

internal sealed partial class ScalingSession
{
    // The worker holds this across GPU execution and the matching UI commit.
    // Async UI export waits without blocking that commit, then reads completed
    // stage buffers while the worker cannot overwrite them with a new frame.
    private readonly SemaphoreSlim _stageGate=new(1,1);
    private EngineHandle? _stageEngine;
    private NativeEffectStep[] _displayedStages=[];
    private bool _stageMenuOpen,_stageExportBusy;
    private MenuFlyout? _stageMenu;
    private void ConfigureStageExport(Button screenshot)
    {
        var menu=new StageExportFlyout();_stageMenu=menu;screenshot.ContextFlyout=menu;
        Controls.PopupAlignment.Track(screenshot,menu);
        menu.Opening+=(_,_)=> { _stageMenuOpen=true;RefreshStageMenu(menu);UpdateToolbarVisibility(); };
        menu.Closed+=(_,_)=> { _stageMenuOpen=false;UpdateToolbarVisibility(); };
        ToolTip.SetTip(screenshot,Localization.Get("Overlay_Toolbar_TakeScreenshot")+" ("+RuntimeSettings.Current.ScreenshotShortcut.Trigger+")\n"+Localization.Get("Overlay_Toolbar_TakeScreenshot_Description"));
    }
    private void RefreshStageMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header=Localization.Get("Overlay_Toolbar_TakeScreenshot_PopupTitle"),IsEnabled=false });
        menu.Items.Add(new Separator());
        for(int i=0;i<_displayedStages.Length;i++)
        {
            int index=i;var stage=_displayedStages[i];
            var item=new MenuItem { Header=$"{i+1}. {EffectName(stage.Effect)} · {stage.Width}×{stage.Height}",IsEnabled=!_stageExportBusy,Tag=i };
            if(RuntimeSettings.Current.DeveloperMode&&PassExportCatalog.All.TryGetValue(stage.Effect,out var passes)&&passes.Length>1)
            {
                for(int pass=0;pass<passes.Length;pass++)
                {
                    int selectedPass=pass;var description=passes[pass];
                    var entry=new MenuItem { Header=description.Name,Tag=pass };
                    if(description.Outputs.Length==1)
                        entry.Click+=async(_,_)=>await ExportStageAsync(index,stage.Effect,selectedPass,0);
                    else for(uint output=0;output<description.Outputs.Length;output++)
                    {
                        uint selectedOutput=output;
                        var texture=new MenuItem { Header=description.Outputs[output].Name,Tag=output };
                        texture.Click+=async(_,_)=>await ExportStageAsync(index,stage.Effect,selectedPass,selectedOutput);
                        entry.Items.Add(texture);
                    }
                    item.Items.Add(entry);
                }
            }
            else item.Click+=async(_,_)=>await ExportStageAsync(index,stage.Effect);
            menu.Items.Add(item);
        }
        if(_displayedStages.Length==0)menu.Items.Add(new MenuItem { Header=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"等待算法输出":"Waiting for processed output",IsEnabled=false });
    }
    private void SetDisplayedStages(EngineHandle engine,NativeEffectStep[] stages)
    { _stageEngine=engine;_displayedStages=(NativeEffectStep[])stages.Clone(); }
    private async Task<string?> ExportStageAsync(int index,uint effect,int? pass=null,uint output=0)
    {
        if(_stageExportBusy||_stop.IsCancellationRequested||_sourceSuspended)return null;
        _stageExportBusy=true;bool entered=false;
        try
        {
            await _stageGate.WaitAsync(_stop.Token);entered=true;
            _stop.Token.ThrowIfCancellationRequested();
            if(_sourceSuspended)return null;
            if(_stageEngine is not { IsClosed:false,IsInvalid:false } engine||index<0||index>=_displayedStages.Length||_displayedStages[index].Effect!=effect)
                throw new InvalidOperationException(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"效果链已改变，请重新选择要导出的结果。":"The effect chain changed. Select the result again.");
            if(pass.HasValue&&!RuntimeSettings.Current.DeveloperMode)throw new InvalidOperationException("Developer mode is disabled. Reopen the screenshot menu.");
            var stage=_displayedStages[index];var source=ReadScreenshotSource();
            var result=await Task.Run(()=>
            {
                var snapshots=StageSnapshot.Read(engine);
                if(snapshots.Length!=_displayedStages.Length||snapshots[index].Effect!=effect||snapshots[index].Width!=stage.Width||snapshots[index].Height!=stage.Height)
                    throw new InvalidOperationException("Stage output does not match the submitted frame.");
                if(!pass.HasValue)return (Pixels:snapshots[index].Copy(engine),Info:(PassSnapshot?)null);
                var info=PassSnapshot.Read(engine,(uint)index,(uint)pass.Value,output);
                if(info.Serial!=snapshots[index].Serial||!PassExportCatalog.All.TryGetValue(effect,out var descriptions)||pass.Value<0||pass.Value>=descriptions.Length||output>=descriptions[pass.Value].Outputs.Length)
                    throw new InvalidOperationException("Pass output metadata differs from the selected graph.");
                var expected=descriptions[pass.Value].Outputs[output];
                if(info.Texture!=(expected.Texture<0?uint.MaxValue:(uint)expected.Texture)||info.Format!=expected.Format)
                    throw new InvalidOperationException("Native/managed output texture identities differ.");
                return (Pixels:info.Copy(engine),Info:(PassSnapshot?)info);
            },_stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            string suffix=$"stage{index+1}_{EffectName(effect)}";
            uint width=stage.Width,height=stage.Height;
            if(result.Info is {} selected)
            {
                string texture=PassExportCatalog.All[effect][pass!.Value].Outputs[output].Name;
                suffix+=$"_pass{pass.Value+1}_{texture}";
                if(selected.Texture!=uint.MaxValue)return SavePassDds(selected,result.Pixels,source,suffix);
                width=selected.Width;height=selected.Height;
            }
            using var bitmap=new WriteableBitmap(new PixelSize((int)width,(int)height),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Opaque);
            FillStageBitmap(bitmap,result.Pixels);
            return SaveScreenshotBitmap(bitmap,source,suffix);
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested) { return null; }
        catch(Exception failure)
        {
            if(_message is not null) { _message.Text=(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"中间结果导出失败：":"Intermediate export failed: ")+failure.Message;_message.IsVisible=true; }
            return null;
        }
        finally { if(entered)_stageGate.Release();_stageExportBusy=false; }
    }
    private string SavePassDds(PassSnapshot info,byte[] pixels,CaptureSource source,string suffix)
    {
        string directory=_settings.ResolveScreenshotDirectory();Directory.CreateDirectory(directory);
        string name=ScreenshotNames.Format(_settings.ResolveScreenshotTemplate(),source.ScreenshotTitle,source.ScreenshotProcess,DateTime.Now);
        name=Path.ChangeExtension(ScreenshotNames.WithSuffix(name,suffix),".dds");
        string path=Path.Combine(directory,name),temporary=Path.Combine(directory,".magpie-export-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))DdsExport.Write(file,info,pixels);
            for(int index=1;;index++)
            {
                try { File.Move(temporary,path,false);break; }
                catch(IOException)when(File.Exists(path)) { path=Path.Combine(directory,Path.GetFileNameWithoutExtension(name)+"_"+index+".dds"); }
            }
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
        if(_message is not null) { _message.Text="Saved "+path;_message.IsVisible=true; }
        return path;
    }
    private static unsafe void FillStageBitmap(WriteableBitmap bitmap,byte[] pixels)
    {
        using var destination=bitmap.Lock();int row=checked(bitmap.PixelSize.Width*4);
        fixed(byte* source=pixels)
            for(int y=0;y<bitmap.PixelSize.Height;y++)Buffer.MemoryCopy(source+y*row,(byte*)destination.Address+y*destination.RowBytes,destination.RowBytes,row);
    }
}
