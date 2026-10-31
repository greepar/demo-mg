using Avalonia.Threading;
using Magpie.Desktop.Core;
using Magpie.Desktop.Views;
namespace Magpie.Desktop;

public partial class MainWindow
{
    private readonly ScalingCountdown _countdown=new();
    private DispatcherTimer? _countdownTimer;
    private Func<RuntimeSettings,ScalingSession>? _previewSessionFactory;
    private ScalingSession CreateScalingSession()
    {
        if(_previewSessionFactory is not {} factory)
        {
            var root=RuntimeSettings.Current;
            if(OperatingSystem.IsWindows()&&root.Profiles.Any(p=>p.OriginalProfile.HasValue))
            {
                try
                {
                    var selected=WindowsCaptureSelection.Read();
                    return new ScalingSession(WindowsProfileMatching.ResolveManual(root,selected?.Identity),selectedSource:selected);
                }
                catch(Exception error) {Console.Error.WriteLine("Application identity: "+error.Message);}
                return new ScalingSession(root);
            }
            return new ScalingSession(root.ActiveSettings);
        }
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Countdown fixtures require isolated preview mode.");
        return factory(RuntimeSettings.Current.ActiveSettings);
    }
    private void StartCountdown(bool fullscreen,int seconds)
    {
        _countdown.Toggle(fullscreen,seconds);
        if(_countdown.Active)
        {
            _countdownTimer??=new DispatcherTimer(TimeSpan.FromMilliseconds(25),DispatcherPriority.Normal,(_,_)=>TickCountdown());
            _countdownTimer.Start();
        }
        else _countdownTimer?.Stop();
        RefreshCountdown();
    }
    private void CancelCountdown()
    {
        _countdown.Cancel();_countdownTimer?.Stop();RefreshCountdown();
    }
    private void RefreshCountdown()
    {
        if(_pages.TryGetValue("Home",out var page))((HomePage)page).SetCountdown(_countdown.Active?_countdown.Fullscreen:null,_countdown.LabelSeconds,_countdown.Progress);
    }
    private void TickCountdown()
    {
        if(_countdown.Complete(out bool fullscreen))
        {
            _countdownTimer?.Stop();RefreshCountdown();StartScaling(fullscreen,0);
        }
        else RefreshCountdown();
    }
}
