using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifyPresentationAsync(Window owner,uint effect=11)
    {
        string directory=Path.Combine(Path.GetTempPath(),"magpie-presentation-"+Guid.NewGuid().ToString("N"));
        var settings=_settings;uint oldEffect=settings.Effect;string? oldMode=settings.SelectedModeId;string oldDirectory=settings.ScreenshotDirectory,oldTemplate=settings.ScreenshotTemplate;
        try
        {
            settings.Effect=effect;settings.SelectedModeId=null;settings.ScreenshotDirectory=directory;settings.ScreenshotTemplate="presentation";
            using var engine=NativeCore.CreateForSettings(settings);if(engine.IsInvalid)throw NativeCore.Error();
            bool fp16=NativeCore.EffectUsesFP16(engine,effect)!=0;
            if(fp16!=(!RuntimeSettings.Current.DisableFP16&&NativeCore.FP16Supported(engine)!=0&&GraphCatalog.Find(effect)!.SupportsFP16))throw new InvalidOperationException("Output engine precision differs from the user preference/device/effect.");
            OpenOutput(owner,false);_output!.Width=481;_output.Height=321;await Task.Delay(150);
            var selector=_output.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="OutputEffectSelector");
            if(selector.SelectedIndex!=(int)effect||selector.ItemCount!=ScalingModeInterchange.EffectPaths.Length)throw new InvalidOperationException("Output effect selector is missing the active effect.");
            if(effect is >=20 and <=23)
            {
                for(int preset=20;preset<=23;preset++) { selector.SelectedIndex=preset;if(settings.Effect!=preset||settings.ResolveChain(3,2,481,321)[0].Width!=3)throw new InvalidOperationException("Output SMAA selection did not update the same-size graph."); }
                selector.SelectedIndex=(int)effect;
            }
            if(GraphEffects.HasParameters(effect))
            {
                var button=_output.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="OutputGraphParameters");
                if(!button.IsVisible)throw new InvalidOperationException("Output filter parameter entry is hidden.");
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);
                var editor=(Control)(_graphParameterFlyout?.Content??throw new InvalidOperationException("Output filter parameter flyout did not open."));
                var definition=GraphEffects.Definitions(effect)[0];
                decimal changed=effect==24?.65m:effect==25?1.5m:Math.Min((decimal)definition.Maximum,(decimal)definition.Default+definition.Step);
                var number=editor.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>n.Name=="GraphParameter0");number.Value=changed;
                if(settings.ResolveChain(3,2,481,321)[0].Parameter1!=(float)changed)throw new InvalidOperationException("Output filter parameter did not reach the graph.");
                _graphParameterFlyout!.Hide();
            }
            var processor=new ScalingFrameProcessor(0);
            byte[] source=[17,31,47,255,61,79,97,255,113,127,139,255,151,167,181,255,193,211,223,255,229,239,251,255];
            var graph=PresentationPlan.ForWindow(settings,3,2,481,321,true);
            if(graph.Length!=2 || graph[0].Effect!=effect || graph[^1].Width!=481 || graph[^1].Height!=321 || !ReplayFrame(processor,engine,source,graph,true,0,gpu:true))throw new InvalidOperationException("Window presentation did not execute CNN and Bicubic.");
            Present(processor.Pixels,graph[^1].Width,graph[^1].Height);await Task.Delay(60);
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Presentation screenshot failed."),processor.Pixels,new PixelSize(481,321));
            if(ReplayFrame(processor,engine,source,graph,true,1,gpu:true))throw new InvalidOperationException("Duplicate presentation frame reached Vulkan.");
            // Resize a static source to a different aspect ratio; the renderer remains proportional.
            _output.Width=482;_output.Height=400;await Task.Delay(120);
            graph=PresentationPlan.ForWindow(settings,3,2,482,400,true);
            if(graph[^1].Width!=482 || graph[^1].Height!=321 || !ReplayFrame(processor,engine,source,graph,false,2,gpu:true))throw new InvalidOperationException("Idle resize did not update the presentation viewport.");
            Present(processor.Pixels,482,321);await Task.Delay(60);
            var displayedImage=_image ?? throw new InvalidOperationException("Output image is missing.");
            var location=displayedImage.TranslatePoint(default,(Visual)_output.Content!) ?? throw new InvalidOperationException("Output image has no layout position.");
            if(Math.Abs(location.X*_output.RenderScaling)>1 || Math.Abs(location.Y*_output.RenderScaling-39)>1 || Math.Abs(displayedImage.Bounds.Width*_output.RenderScaling-482)>1 || Math.Abs(displayedImage.Bounds.Height*_output.RenderScaling-321)>1)throw new InvalidOperationException("Presentation image is stretched or centered on fractional pixels: "+location+" "+displayedImage.Bounds);
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Resized presentation screenshot failed."),processor.Pixels,new PixelSize(482,321));
            var fullscreenPlan=PresentationPlan.ForWindow(settings,3,2,482,400,false);
            if(fullscreenPlan.Length!=1 || fullscreenPlan[0].Width!=settings.ResolveChain(3,2,482,400,false)[0].Width)throw new InvalidOperationException("Fullscreen magnified a smaller fixed-output effect.");
            if(_statistics is not null)_statistics.Text=EffectStageSettings.Name(effect)+" → automatic Bicubic (B=0, C=0.5) · proportional renderer · 39 px top border";
            var arguments=Environment.GetCommandLineArgs();int capture=Array.IndexOf(arguments,"--capture");
            if(capture>=0 && capture+1<arguments.Length)
            {
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(723,600),new Vector(144,144));bitmap.Render((Control)_output.Content!);
                bitmap.Save(Path.ChangeExtension(arguments[capture+1],"output.png"),Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            Console.WriteLine("PRESENT OUTPUT PASS: effect="+effect+", precision="+(fp16?"Float16":"Float32")+", actual effect/Bicubic GPU replay, duplicate skip, static aspect resize, integer image bounds and lossless screenshots; no capture picker.");
        }
        finally
        {
            settings.Effect=oldEffect;settings.SelectedModeId=oldMode;settings.ScreenshotDirectory=oldDirectory;settings.ScreenshotTemplate=oldTemplate;
            _output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_finished=true;_stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
