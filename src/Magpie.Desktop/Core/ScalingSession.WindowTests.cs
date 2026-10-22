using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private static async Task WaitForWindowStateAsync(Func<bool> ready,string description)
    {
        var deadline=System.Diagnostics.Stopwatch.StartNew();
        while(!ready())
        {
            if(deadline.Elapsed>TimeSpan.FromSeconds(5))throw new InvalidOperationException("Timed out waiting for "+description);
            await Task.Delay(20);
        }
    }
    internal async Task VerifyLargeSourceWindowAsync(Window owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Large-source fixtures require isolated preview mode.");
        const uint sourceWidth=3840,sourceHeight=2160;
        try
        {
            OpenOutput(owner,false,sourceWidth,sourceHeight);await Task.Delay(200);
            var output=_output ?? throw new InvalidOperationException("Large-source output is missing.");
            var screen=output.Screens.ScreenFromWindow(output) ?? output.Screens.Primary!;
            var frame=output.FrameSize ?? output.ClientSize;
            if(output.WindowState!=WindowState.Normal || Volatile.Read(ref _windowed)!=1 || !_initialSizingApplied || frame.Width*output.RenderScaling>=screen.WorkingArea.Width || frame.Height*output.RenderScaling>=screen.WorkingArea.Height)
                throw new InvalidOperationException("Large capture did not open as a normal window inside the display work area.");
            var initial=output.ClientSize;
            output.Width=initial.Width*.75;output.Height=initial.Height*.75;await Task.Delay(160);
            if(output.WindowState!=WindowState.Normal || output.ClientSize.Width>=initial.Width || output.ClientSize.Height>=initial.Height)
                throw new InvalidOperationException("Large-source minimum prevented resizing the window down.");
            using var engine=NativeCore.CreateForDevice(_settings.GraphicsDeviceId);if(engine.IsInvalid)throw NativeCore.Error();
            byte[] source=new byte[sourceWidth*sourceHeight*4];for(int i=0;i<source.Length;i+=4) { source[i]=17;source[i+1]=79;source[i+2]=151;source[i+3]=255; }
            long target=Interlocked.Read(ref _target);
            var graph=PresentationPlan.ForWindow(_settings,sourceWidth,sourceHeight,(uint)(target>>32),(uint)target,true);
            var processor=new ScalingFrameProcessor(0);
            unsafe
            {
                fixed(byte* pixels=source)
                    if(!processor.Process(engine,(nint)pixels,sourceWidth,sourceHeight,true,graph,DuplicateFrameMode.Always,false,0,0,0))throw new InvalidOperationException("Large captured source did not reach Vulkan.");
            }
            for(int i=0;i<processor.Pixels.Length;i++)if(Math.Abs(processor.Pixels[i]-source[i%4])>1)throw new InvalidOperationException("Downscaled large source changed constant pixel values.");
            Present(processor.Pixels,graph[^1].Width,graph[^1].Height);await Task.Delay(60);
            if(output.WindowState!=WindowState.Normal)throw new InvalidOperationException("Presentation forced a large-source window fullscreen.");
            Console.WriteLine("LARGE SOURCE WINDOW PASS: 3840x2160 capture fits below the work area, remains normal, resizes smaller and presents verified Vulkan downscaled pixels.");
        }
        finally { _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose(); }
    }
    private static unsafe bool ReplayWindowSource(ScalingFrameProcessor processor,EngineHandle engine,byte[] source,NativeEffectStep[] graph,bool fresh,long time)
    {
        fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,800,450,fresh,graph,DuplicateFrameMode.Always,false,0,0,time);
    }
    internal async Task VerifyWindowSettingsAsync(Window owner,uint expectedWidth,uint expectedHeight)
    {
        var old=(_settings.InitialWindowedScaleFactor,_settings.OutputAlignment,_settings.Effect,_settings.SelectedModeId,_settings.ScaleType,_settings.AbsoluteWidth,_settings.AbsoluteHeight,_settings.ScreenshotDirectory,_settings.ScreenshotTemplate);
        string directory=Path.Combine(Path.GetTempPath(),"magpie-window-output-"+Guid.NewGuid().ToString("N"));
        try
        {
            _settings.ScreenshotDirectory=directory;_settings.ScreenshotTemplate="window";
            using var engine=NativeCore.CreateForDevice(_settings.GraphicsDeviceId);if(engine.IsInvalid)throw NativeCore.Error();
            OpenOutput(owner,false,800,450);await Task.Delay(160);
            var output=_output ?? throw new InvalidOperationException("Initial window is missing.");
            long target=Interlocked.Read(ref _target);
            if((uint)(target>>32)!=expectedWidth || (uint)target!=expectedHeight || !_initialSizingApplied || Volatile.Read(ref _windowed)!=1)throw new InvalidOperationException("Initial settings did not size the actual client: "+output.ClientSize+", target="+(uint)(target>>32)+"x"+(uint)target+", scale="+output.RenderScaling+", state="+output.WindowState+", prepared="+_initialSizingApplied);
            byte[] source=new byte[800*450*4];for(int i=0;i<source.Length;i+=4) { source[i]=17;source[i+1]=79;source[i+2]=151;source[i+3]=255; }
            var processor=new ScalingFrameProcessor(0);
            var graph=PresentationPlan.ForWindow(_settings,800,450,expectedWidth,expectedHeight,true);
            if(!ReplayWindowSource(processor,engine,source,graph,true,0))throw new InvalidOperationException("Initial sizing did not reach Vulkan.");
            for(int i=0;i<processor.Pixels.Length;i++)if(Math.Abs(processor.Pixels[i]-source[i%4])>1)throw new InvalidOperationException("Initially scaled constant source changed color.");
            Present(processor.Pixels,graph[^1].Width,graph[^1].Height);VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Initial PNG failed."),processor.Pixels,new PixelSize((int)graph[^1].Width,(int)graph[^1].Height));
            _settings.InitialWindowedScaleFactor=3;ApplyInitialWindowSize(owner);await Task.Delay(30);
            if(Interlocked.Read(ref _target)!=target)throw new InvalidOperationException("Initial preference resized a running window.");
            output.Width=expectedWidth/output.RenderScaling+60;output.Height=expectedHeight/output.RenderScaling+45;await Task.Delay(120);
            var remembered=output.ClientSize;
            target=Interlocked.Read(ref _target);
            graph=PresentationPlan.ForWindow(_settings,800,450,(uint)(target>>32),(uint)target,true);
            if(!ReplayWindowSource(processor,engine,source,graph,false,1))throw new InvalidOperationException("Static source did not resize.");
            Present(processor.Pixels,graph[^1].Width,graph[^1].Height);
            var fullScreen=output.Screens.ScreenFromWindow(output) ?? output.Screens.Primary!;
            output.WindowState=WindowState.FullScreen;
            await WaitForWindowStateAsync(()=>output.WindowState==WindowState.FullScreen && Volatile.Read(ref _windowed)==0 &&
                (uint)(Interlocked.Read(ref _target)>>32)==(uint)fullScreen.Bounds.Width && (uint)Interlocked.Read(ref _target)==(uint)fullScreen.Bounds.Height,"the compositor's fullscreen configure and presentation viewport");
            _settings.Effect=0;_settings.SelectedModeId=null;_settings.ScaleType=2;_settings.AbsoluteWidth=5;_settings.AbsoluteHeight=3;
            target=Interlocked.Read(ref _target);uint width=(uint)(target>>32),height=(uint)target;
            if(Volatile.Read(ref _windowed)!=0)throw new InvalidOperationException("Actual fullscreen state did not reach presentation.");
            graph=PresentationPlan.ForWindow(_settings,800,450,width,height,false);
            if(!ReplayWindowSource(processor,engine,source,graph,false,2))throw new InvalidOperationException("Fullscreen fixed output did not render.");
            Present(processor.Pixels,5,3);_toolbar!.IsVisible=false;
            long rendered=processor.Performance.TotalScaled;
            long[] xs=[0,((long)width-5)/2,(long)width-5],ys=[0,((long)height-3)/2,(long)height-3];
            for(int alignment=0;alignment<9;alignment++)
            {
                _settings.OutputAlignment=alignment;SetSourceGeometry(800,450);await Task.Delay(25);
                var image=_image ?? throw new InvalidOperationException("Aligned image is missing.");
                var point=image.TranslatePoint(default,(Visual)output.Content!) ?? throw new InvalidOperationException("Aligned image has no layout.");
                if(Math.Abs(point.X*output.RenderScaling-xs[alignment%3])>.51 || Math.Abs(point.Y*output.RenderScaling-ys[alignment/3])>.51)throw new InvalidOperationException("Actual output alignment differs at "+alignment+": "+point);
                if(ReplayWindowSource(processor,engine,source,graph,false,3+alignment) || processor.Performance.TotalScaled!=rendered)throw new InvalidOperationException("Alignment-only change invoked Vulkan.");
            }
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Aligned PNG failed."),processor.Pixels,new PixelSize(5,3));
            output.WindowState=WindowState.Normal;
            await WaitForWindowStateAsync(()=>output.WindowState==WindowState.Normal && Volatile.Read(ref _windowed)==1 &&
                Math.Abs(output.ClientSize.Width-remembered.Width)<=1 && Math.Abs(output.ClientSize.Height-remembered.Height)<=1,"the compositor's restored window geometry");
            if(Volatile.Read(ref _windowed)!=1 || Math.Abs(output.ClientSize.Width-remembered.Width)>1 || Math.Abs(output.ClientSize.Height-remembered.Height)>1)throw new InvalidOperationException("Fullscreen transition lost the user's window size.");
            target=Interlocked.Read(ref _target);graph=PresentationPlan.ForWindow(_settings,800,450,(uint)(target>>32),(uint)target,true);
            if(!ReplayWindowSource(processor,engine,source,graph,false,20))throw new InvalidOperationException("Restored window did not replan static source.");
            Present(processor.Pixels,graph[^1].Width,graph[^1].Height);_toolbar.IsVisible=true;
            if(_statistics is not null)_statistics.Text=$"Initial {expectedWidth}×{expectedHeight} · 9 actual alignments · static resize and fullscreen restore";
            await Task.Delay(80);
            var args=Environment.GetCommandLineArgs();int capture=Array.IndexOf(args,"--capture");
            if(capture>=0 && capture+1<args.Length)
            {
                var size=PixelSize.FromSize(output.ClientSize,1);
                using var bitmap=new RenderTargetBitmap(size);bitmap.Render((Control)output.Content!);bitmap.Save(Path.ChangeExtension(args[capture+1],"output.png"),PngBitmapEncoderOptions.Default);
            }
            Console.WriteLine("WINDOW OUTPUT PASS: actual initial client, Vulkan constant pixels/PNG, one-time sizing, static resize, real fullscreen state, 9 integer alignments without GPU rerender, remembered window size and restore.");
        }
        finally
        {
            (_settings.InitialWindowedScaleFactor,_settings.OutputAlignment,_settings.Effect,_settings.SelectedModeId,_settings.ScaleType,_settings.AbsoluteWidth,_settings.AbsoluteHeight,_settings.ScreenshotDirectory,_settings.ScreenshotTemplate)=old;
            _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
