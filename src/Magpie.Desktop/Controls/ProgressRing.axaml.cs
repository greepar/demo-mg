using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
namespace Magpie.Desktop.Controls;
public partial class ProgressRing : UserControl
{
    public ProgressRing()
    {
        InitializeComponent();
        var rotation = new RotateTransform();
        Arc.RenderTransform = rotation;
        var elapsed = new Stopwatch();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => rotation.Angle = elapsed.Elapsed.TotalSeconds * 360 % 360;
        AttachedToVisualTree += (_, _) => { elapsed.Start(); timer.Start(); };
        DetachedFromVisualTree += (_, _) => { timer.Stop(); elapsed.Stop(); };
    }
}
