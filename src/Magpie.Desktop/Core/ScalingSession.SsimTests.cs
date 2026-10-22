using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;
internal sealed partial class ScalingSession
{
    private static unsafe bool ReplaySsim(EngineHandle engine,ScalingFrameProcessor processor,byte[] source,NativeEffectStep[] graph,bool fresh,long time)
    {
        fixed(byte* pixels=source)return processor.Process(engine,(nint)pixels,11,9,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,true);
    }
    private static unsafe byte[] StandaloneSsim(EngineHandle engine,byte[] source,uint width,uint height,float sharpness)
    {
        byte[] expected=new byte[width*height*4];
        fixed(byte* pixels=source,target=expected)
            if(NativeCore.ScaleExtended(engine,(nint)pixels,11,9,target,width,height,14,sharpness,0)==0)throw NativeCore.Error();
        return expected;
    }
    internal async Task VerifySsimOutputAsync(Window owner)
    {
        string directory=Path.Combine(Path.GetTempPath(),"magpie-ssim-output-"+Guid.NewGuid().ToString("N"));
        try
        {
            _settings.Effect=14;_settings.SelectedModeId=null;_settings.SsimOversharp=1.7f;_settings.ScaleType=2;_settings.AbsoluteWidth=5;_settings.AbsoluteHeight=4;
            _settings.ScreenshotDirectory=directory;_settings.ScreenshotTemplate="ssim";
            using var engine=NativeCore.Create();if(engine.IsInvalid)throw NativeCore.Error();
            OpenOutput(owner,true);SetSourceGeometry(11,9);await Task.Delay(150);
            byte[] source=new byte[11*9*4];for(int i=0;i<source.Length;i++)source[i]=i%4==3?(byte)255:(byte)(i*31%251);
            var processor=new ScalingFrameProcessor(0);
            void Render(bool fresh,long time)
            {
                long target=Interlocked.Read(ref _target);
                var graph=PresentationPlan.ForWindow(_settings,11,9,(uint)(target>>32),(uint)target,false);
                if(graph.Length!=1 || graph[0].Effect!=14 || !ReplaySsim(engine,processor,source,graph,fresh,time))throw new InvalidOperationException("Actual SSim graph did not render.");
                var expected=StandaloneSsim(engine,source,graph[0].Width,graph[0].Height,_settings.SsimOversharp);
                if(!expected.SequenceEqual(processor.Pixels))throw new InvalidOperationException("Actual SSim processor pixels differ from direct GPU.");
                Present(processor.Pixels,graph[0].Width,graph[0].Height);
                VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("SSim PNG failed."),expected,new PixelSize((int)graph[0].Width,(int)graph[0].Height));
            }
            Render(true,0);long current=Interlocked.Read(ref _target);
            var same=PresentationPlan.ForWindow(_settings,11,9,(uint)(current>>32),(uint)current,false);
            if(ReplaySsim(engine,processor,source,same,true,1))throw new InvalidOperationException("Duplicate SSim output rerendered.");
            var number=_output!.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>n.Name=="SsimOversharpInput");
            number.Value=3;if(_settings.SsimOversharp!=3)throw new InvalidOperationException("Actual toolbar SSim parameter did not save.");Render(false,2);
            _settings.AbsoluteWidth=7;_settings.AbsoluteHeight=3;Render(false,3);await Task.Delay(80);
            if(processor.Performance.TotalReceived!=2 || processor.Performance.TotalScaled!=3 || _image!.Source!.Size.Width!=7)throw new InvalidOperationException("SSim resize lost output or counted fabricated input.");
            if(_statistics is not null)_statistics.Text="SSimDownscaler · 5 passes · live oversharp and intermediate resize · exact PNG";
            Console.WriteLine("SSIM OUTPUT PASS: actual fullscreen window and toolbar parameter, five-pass GPU output, exact PNG, duplicate skip, static parameter/intermediate resize and accurate counters.");
        }
        finally
        {
            _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
