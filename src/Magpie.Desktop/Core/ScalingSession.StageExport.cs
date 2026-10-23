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
            item.Click+=async(_,_)=>await ExportStageAsync(index,stage.Effect);
            menu.Items.Add(item);
        }
        if(_displayedStages.Length==0)menu.Items.Add(new MenuItem { Header=Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"等待算法输出":"Waiting for processed output",IsEnabled=false });
    }
    private void SetDisplayedStages(EngineHandle engine,NativeEffectStep[] stages)
    { _stageEngine=engine;_displayedStages=(NativeEffectStep[])stages.Clone(); }
    private async Task<string?> ExportStageAsync(int index,uint effect)
    {
        if(_stageExportBusy||_stop.IsCancellationRequested)return null;
        _stageExportBusy=true;bool entered=false;
        try
        {
            await _stageGate.WaitAsync(_stop.Token);entered=true;
            _stop.Token.ThrowIfCancellationRequested();
            if(_stageEngine is not { IsClosed:false,IsInvalid:false } engine||index<0||index>=_displayedStages.Length||_displayedStages[index].Effect!=effect)
                throw new InvalidOperationException(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"效果链已改变，请重新选择要导出的结果。":"The effect chain changed. Select the result again.");
            var stage=_displayedStages[index];var source=ReadScreenshotSource();
            byte[] pixels=await Task.Run(()=>
            {
                var snapshots=StageSnapshot.Read(engine);
                if(snapshots.Length!=_displayedStages.Length||snapshots[index].Effect!=effect||snapshots[index].Width!=stage.Width||snapshots[index].Height!=stage.Height)
                    throw new InvalidOperationException("Stage output does not match the submitted frame.");
                return snapshots[index].Copy(engine);
            },_stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            using var bitmap=new WriteableBitmap(new PixelSize((int)stage.Width,(int)stage.Height),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Opaque);
            FillStageBitmap(bitmap,pixels);
            string suffix=ScreenshotNames.Format("%WT%",$"stage{index+1}_{EffectName(effect)}","",DateTime.Now);
            return SaveScreenshotBitmap(bitmap,source,Path.GetFileNameWithoutExtension(suffix));
        }
        catch(OperationCanceledException)when(_stop.IsCancellationRequested) { return null; }
        catch(Exception failure)
        {
            if(_message is not null) { _message.Text=(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"中间结果导出失败：":"Intermediate export failed: ")+failure.Message;_message.IsVisible=true; }
            return null;
        }
        finally { if(entered)_stageGate.Release();_stageExportBusy=false; }
    }
    private static unsafe void FillStageBitmap(WriteableBitmap bitmap,byte[] pixels)
    {
        using var destination=bitmap.Lock();int row=checked(bitmap.PixelSize.Width*4);
        fixed(byte* source=pixels)
            for(int y=0;y<bitmap.PixelSize.Height;y++)Buffer.MemoryCopy(source+y*row,(byte*)destination.Address+y*destination.RowBytes,destination.RowBytes,row);
    }
}
