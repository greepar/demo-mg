using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Magpie.Desktop.Core;
using Magpie.Desktop.Views;
namespace Magpie.Desktop;

public partial class MainWindow
{
    internal async Task VerifyCountdownAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Countdown fixtures require isolated preview mode.");
        var home=(HomePage)_pages["Home"];int entered=0;
        _previewSessionFactory=settings=>ScalingSession.CountdownFixture(settings,()=>Interlocked.Increment(ref entered));
        Button Button(string name)=>home.FindControl<Button>(name)!;
        void Click(string name)=>Button(name).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        async Task Wait(Func<bool> ready)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(!ready())await Task.Delay(20,deadline.Token);
        }
        try
        {
            home.FindControl<Slider>("DelaySeconds")!.Value=3;
            Click("FullscreenStart");await Task.Delay(100);
            if(!_countdown.Active||!_countdown.Fullscreen||_scalingSession is not null||!home.FindControl<Grid>("CountdownDisplay")!.IsVisible||home.FindControl<Controls.CountdownRing>("CountdownProgress")!.Value>=1)throw new InvalidOperationException("Start button did not display live countdown before capture.");
            Click("WindowedStart");await Task.Delay(40);
            if(!_countdown.Active||_countdown.Fullscreen||_countdown.LabelSeconds!=3)throw new InvalidOperationException("Other-mode button did not restart countdown.");
            Click("WindowedStart");await Task.Delay(40);
            if(_countdown.Active||_countdownTimer!.IsEnabled||home.FindControl<Grid>("CountdownDisplay")!.IsVisible||entered!=0)throw new InvalidOperationException("Cancel button did not stop countdown without opening capture.");
            Click("FullscreenStart");StartScaling(false,0);
            await Wait(()=>entered==1);
            if(_countdown.Active||_scalingSession is null)throw new InvalidOperationException("Immediate shortcut did not replace pending countdown.");
            StopScaling();await Wait(()=>_scalingSession is null);
            home.FindControl<Slider>("DelaySeconds")!.Value=1;Click("WindowedStart");
            await Wait(()=>entered==2);await Task.Delay(150);
            if(_countdown.Active||_countdownTimer!.IsEnabled||_scalingSession is null||entered!=2)throw new InvalidOperationException("Expired countdown did not start exactly once.");
            StopScaling();await Wait(()=>_scalingSession is null);
            home.FindControl<Slider>("DelaySeconds")!.Value=3;Click("WindowedStart");StopScaling();
            await Task.Delay(100);
            if(_countdown.Active||_countdownTimer!.IsEnabled||entered!=2)throw new InvalidOperationException("Stop left countdown callbacks pending.");
            Click("FullscreenStart");await Task.Delay(180);
            _countdownTimer?.Stop();
            var display=home.FindControl<Grid>("CountdownDisplay")!;
            display.BringIntoView();await Task.Delay(80);
            if(display.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is {} scroll)
                scroll.Offset=new Avalonia.Vector(scroll.Offset.X,scroll.Offset.Y+48);
            await Task.Delay(60);
            Console.WriteLine("COUNTDOWN UI PASS: actual start/cancel/mode buttons, progress display, immediate shortcut replacement, natural expiry once, session cleanup and stop; all captures synthetic.");
        }
        finally
        {
            // Freeze the final visual countdown before removing the capture
            // fixture, so a slow screenshot can never reach a real source picker.
            _countdownTimer?.Stop();
            if(_scalingSession is not null)StopScaling();
            _previewSessionFactory=null;
        }
    }
}
