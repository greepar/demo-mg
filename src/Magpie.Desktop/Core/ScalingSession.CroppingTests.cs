using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private static unsafe bool ReplayCroppedSource(EngineHandle engine,ScalingFrameProcessor processor,byte[] pixels,SourceRegion crop,NativeEffectStep[] graph,bool fresh,long time)
    {
        fixed(byte* source=pixels)return processor.Process(engine,(nint)source,800,450,fresh,graph,DuplicateFrameMode.Always,false,0,0,time,false,crop);
    }
    internal async Task VerifyCroppingAsync(Window owner)
    {
        // A private settings object avoids modifying defaults or any user profile.
        string directory=Path.Combine(Path.GetTempPath(),"magpie-crop-output-"+Guid.NewGuid().ToString("N"));
        try
        {
            _settings.InitialWindowedScaleFactor=1;_settings.IsCroppingEnabled=true;
            _settings.CroppingLeft=30;_settings.CroppingRight=50;_settings.CroppingTop=20;_settings.CroppingBottom=30;
            _settings.ScreenshotDirectory=directory;_settings.ScreenshotTemplate="crop";
            _settings.SelectedModeId=null;_settings.Effect=0;_settings.ScaleType=0;_settings.ScaleX=_settings.ScaleY=1;
            OpenCapturedOutput(owner,false,800,450);await Task.Delay(160);
            long target=Interlocked.Read(ref _target);
            if((uint)(target>>32)!=900 || (uint)target!=500 || _sourceWidth!=720 || _sourceHeight!=400)throw new InvalidOperationException("First captured crop did not size the real window.");
            _output!.WindowState=WindowState.FullScreen;await Task.Delay(120);
            using var engine=NativeCore.CreateForDevice(_settings.GraphicsDeviceId);if(engine.IsInvalid)throw NativeCore.Error();
            byte[] source=new byte[800*450*4];
            for(int y=0;y<450;y++)for(int x=0;x<800;x++) { int i=(y*800+x)*4;source[i]=(byte)(x%256);source[i+1]=(byte)(y%256);source[i+2]=(byte)((x+y)%256);source[i+3]=255; }
            var processor=new ScalingFrameProcessor(0);
            void Verify(bool fresh,long time)
            {
                var crop=PrepareCapturedSource(800,450);long viewport=Interlocked.Read(ref _target);
                var graph=PresentationPlan.ForWindow(_settings,crop.Width,crop.Height,(uint)(viewport>>32),(uint)viewport,false);
                if(!ReplayCroppedSource(engine,processor,source,crop,graph,fresh,time))throw new InvalidOperationException("Changed crop did not render.");
                byte[] expected=new byte[crop.Width*crop.Height*4];
                for(int y=0;y<crop.Height;y++)Array.Copy(source,((y+(int)crop.Y)*800+(int)crop.X)*4,expected,y*(int)crop.Width*4,(int)crop.Width*4);
                if(!processor.Pixels.SequenceEqual(expected))throw new InvalidOperationException("Live crop path changed RGBA pixels.");
                Present(processor.Pixels,crop.Width,crop.Height);
                VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Crop screenshot failed."),expected,new PixelSize((int)crop.Width,(int)crop.Height));
            }
            Verify(true,0);
            _settings.CroppingLeft=31;_settings.CroppingRight=49;Verify(false,1);
            _settings.IsCroppingEnabled=false;Verify(false,2);
            if(_sourceWidth!=800 || _sourceHeight!=450 || processor.Performance.TotalReceived!=1 || processor.Performance.TotalScaled!=3)throw new InvalidOperationException("Crop disable lost source geometry or invented frames.");
            Console.WriteLine("CROPPING OUTPUT PASS: shared first-frame capture sizing, actual fullscreen presentation, exact cropped PNG, same-size static crop move and disable without new input frames.");
        }
        finally
        {
            _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
