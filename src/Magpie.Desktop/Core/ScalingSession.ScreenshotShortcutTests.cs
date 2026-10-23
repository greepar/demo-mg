using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifyScreenshotShortcutAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Shortcut fixtures require isolated preview mode.");
        string folder=Path.Combine(Path.GetTempPath(),"magpie-screenshot-shortcut-"+Guid.NewGuid().ToString("N"));
        var previous=RuntimeSettings.Current.ScreenshotShortcut;
        try
        {
            _settings.ScreenshotDirectory=folder;_settings.ScreenshotTemplate="shortcut";
            RuntimeSettings.Current.ScreenshotShortcut=new() { Key="F10",Modifiers="Ctrl,Shift" };
            OpenOutput(owner,false,3,2);await Task.Delay(80);
            byte[] pixels=[1,2,3,255,5,7,11,255,13,17,19,255,23,29,31,255,37,41,43,255,47,53,59,255];
            if(!Present(pixels,3,2))throw new InvalidOperationException("Shortcut bitmap presentation failed.");
            int Count()=>Directory.Exists(folder)?Directory.GetFiles(folder,"*.png").Length:0;
            KeyEventArgs SendKey(Key key,KeyModifiers modifiers)
            {
                var args=new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=key,KeyModifiers=modifiers };
                _output!.RaiseEvent(args);return args;
            }
            SendKey(Key.S,KeyModifiers.Alt|KeyModifiers.Shift);DrainScreenshot();
            if(Count()!=0)throw new InvalidOperationException("Old screenshot preference remained active.");
            var accepted=SendKey(Key.F10,KeyModifiers.Control|KeyModifiers.Shift);DrainScreenshot();
            if(!accepted.Handled||Count()!=1)throw new InvalidOperationException("Configured screenshot key did not save a PNG.");
            SendKey(Key.F10,KeyModifiers.Control);DrainScreenshot();
            if(Count()!=1)throw new InvalidOperationException("Mismatched screenshot modifiers were accepted.");
            GlobalScreenshotShortcutActive=()=>true;
            SendKey(Key.F10,KeyModifiers.Control|KeyModifiers.Shift);DrainScreenshot();
            if(Count()!=1)throw new InvalidOperationException("A globally registered screenshot also executed locally.");
            RequestScreenshot();RequestScreenshot();DrainScreenshot();
            if(Count()!=2)throw new InvalidOperationException("Screenshot requests did not coalesce before frame processing.");
            SendKey(Key.F12,KeyModifiers.None);DrainScreenshot();
            if(Count()!=3)throw new InvalidOperationException("Legacy output F12 stopped working.");
            foreach(string path in Directory.GetFiles(folder,"*.png"))VerifyPng(path,pixels,new PixelSize(3,2));
            Stop();RequestScreenshot();
            if(Volatile.Read(ref _screenshotPending)!=0)throw new InvalidOperationException("A stopped capture accepted a screenshot request.");
            Console.WriteLine("SCREENSHOT SHORTCUT UI PASS: actual routed keys, saved custom binding/modifiers, stale preference rejection, global/local deduplication, queued request coalescing, F12 compatibility, exact PNG bytes and stopped-session rejection; no system input injected.");
        }
        finally
        {
            RuntimeSettings.Current.ScreenshotShortcut=previous;_finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stop.Dispose();
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
