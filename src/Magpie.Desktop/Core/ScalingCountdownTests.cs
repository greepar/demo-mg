using System.Diagnostics;
namespace Magpie.Desktop.Core;

internal static class ScalingCountdownTests
{
    internal static void Verify()
    {
        long time=0;var timer=new ScalingCountdown(()=>time);
        long Second=Stopwatch.Frequency;
        timer.Toggle(true,3);
        if(!timer.Active||!timer.Fullscreen||timer.LabelSeconds!=3||timer.Progress!=1)throw new InvalidOperationException("Countdown did not start at its saved duration.");
        time=Second+Second/2;
        if(timer.LabelSeconds!=2||Math.Abs(timer.Progress-.5)>1e-6)throw new InvalidOperationException("Countdown did not use elapsed monotonic time.");
        timer.Toggle(false,5);
        if(!timer.Active||timer.Fullscreen||timer.LabelSeconds!=5||timer.Progress!=1)throw new InvalidOperationException("Switching countdown mode did not reset its duration.");
        timer.Toggle(false,5);
        if(timer.Active||timer.Complete(out _))throw new InvalidOperationException("Same-mode cancel left a delayed start pending.");
        timer.Toggle(true,1);time+=Second*10;
        if(!timer.Complete(out bool fullscreen)||!fullscreen||timer.Active||timer.Complete(out _))throw new InvalidOperationException("Delayed timer tick did not complete exactly once.");
        timer.Toggle(false,2);timer.Cancel();time+=Second*10;
        if(timer.Complete(out _))throw new InvalidOperationException("Cancelled countdown completed later.");
        foreach(int invalid in new[]{-1,0,6})
            try { timer.Toggle(true,invalid);throw new Exception("Invalid countdown duration accepted."); }catch(InvalidDataException) { }
        Console.WriteLine("COUNTDOWN PASS: monotonic duration/progress/ceil, mode reset, same-mode cancellation, delayed tick exactly once, stop and invalid duration.");
    }
}
