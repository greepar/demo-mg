using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifySourceScreenshotAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Screenshot fixtures require isolated preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-source-png-"+Guid.NewGuid().ToString("N"));
        try
        {
            _settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="%WT%_%PN%";
            OpenOutput(owner,false,3,2);await Task.Delay(100);
            byte[] pixels=[17,31,59,255,21,49,61,255,83,89,97,255,7,13,19,255,139,149,157,255,211,223,227,255];
            Present(pixels,3,2);
            _lastSourceInfo=new("文档/😀","source-app","/apps/source-app",123,456,1);
            string first=SaveScreenshot()??throw new InvalidOperationException("Source-named screenshot failed.");
            if(Path.GetFileName(first)!="文档_😀_source-app.png")throw new InvalidOperationException("Screenshot did not use selected source title/process.");
            VerifyPng(first,pixels,new PixelSize(3,2));
            string second=SaveScreenshot()!;
            if(Path.GetFileName(second)!="文档_😀_source-app_1.png")throw new InvalidOperationException("Source-named screenshot overwrote a previous file.");
            _lastSourceInfo=new("Changed title","next-app","",0,0,1);
            string changed=SaveScreenshot()!;
            if(Path.GetFileName(changed)!="Changed title_next-app.png")throw new InvalidOperationException("Changed source metadata did not reach the screenshot template.");
            _lastSourceInfo=new("","","",0,0,2);
            string fallback=SaveScreenshot()!;
            string expected=ScreenshotNames.Format("%WT%_%PN%",_lastSourceInfo.ScreenshotTitle,_lastSourceInfo.ScreenshotProcess,DateTime.Now);
            if(Path.GetFileName(fallback)!=expected || Path.GetFileName(fallback).Contains("Magpie"))throw new InvalidOperationException("Unknown source fabricated the capture application's process name.");
            VerifyPng(fallback,pixels,new PixelSize(3,2));
            Console.WriteLine("SOURCE SCREENSHOT UI PASS: actual PNG path/title/process tokens, Unicode/invalid-character sanitization, collision protection, changed metadata and truthful unknown fallback; selected-source metadata is injected fixture data.");
        }
        finally
        {
            _finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
