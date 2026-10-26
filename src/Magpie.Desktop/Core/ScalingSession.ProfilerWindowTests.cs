using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private async Task VerifyFloatingProfilerGesturesAsync()
    {
        var panel=_profilerWindow!;var header=_profilerHeader!;var layer=_profilerLayer!;
        var expected=RuntimeSettings.Current.ProfilerPlacement.Resolve(layer.Bounds.Size,panel.Bounds.Size);
        if(Math.Abs(Canvas.GetLeft(panel)-expected.X)>1||Math.Abs(Canvas.GetTop(panel)-expected.Y)>1)throw new InvalidOperationException("Floating profiler did not use the persisted edge/center placement.");
        var pointer=new Pointer(57,PointerType.Mouse,true);
        var root=header.GetVisualAncestors().Last();
        Point PointInRoot(Point point)=>layer.TranslatePoint(point,root)??throw new InvalidOperationException("Profiler has no presented root.");
        var start=new Point(Canvas.GetLeft(panel)+10,Canvas.GetTop(panel)+10);
        var down=new PointerPressedEventArgs(header,pointer,root,PointInRoot(start),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1);
        header.RaiseEvent(down);
        if(!down.Handled||_profilerDragStart is null)throw new InvalidOperationException("Profiler header did not begin local drag.");
        header.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,header,pointer,root,PointInRoot(new(-100,-100)),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        if(Canvas.GetLeft(panel)!=0||Canvas.GetTop(panel)!=0)throw new InvalidOperationException("Profiler drag escaped the content boundary.");
        header.RaiseEvent(new PointerReleasedEventArgs(header,pointer,root,PointInRoot(new(-100,-100)),0,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        if(_profilerDragStart is not null||RuntimeSettings.Current.ProfilerPlacement!=new OverlayPlacement(0,0,0,0))throw new InvalidOperationException("Profiler drop did not persist/clamp the new placement.");
        await Task.Delay(60);
        // Resize preserves the stored anchoring semantics, not an absolute
        // desktop position. No native window move API is involved.
        RuntimeSettings.Current.ProfilerPlacement=new();PositionProfiler();await Task.Delay(60);
        var before=RuntimeSettings.Current.ProfilerPlacement;
        start=new(Canvas.GetLeft(panel)+10,Canvas.GetTop(panel)+10);
        header.RaiseEvent(new PointerPressedEventArgs(header,pointer,root,PointInRoot(start),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1));
        header.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,header,pointer,root,PointInRoot(new(15,15)),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        EndProfilerDrag(false);await Task.Delay(60);
        if(RuntimeSettings.Current.ProfilerPlacement!=before||_profilerDragStart is not null)throw new InvalidOperationException("Cancelled profiler drag saved a new position or retained capture.");
        expected=before.Resolve(layer.Bounds.Size,panel.Bounds.Size);
        if(Math.Abs(Canvas.GetLeft(panel)-expected.X)>1||Math.Abs(Canvas.GetTop(panel)-expected.Y)>1)throw new InvalidOperationException("Cancelled drag did not restore persisted placement.");
        double width=_output!.Width,height=_output.Height;
        _output.Width=width+40;_output.Height=height+30;await Task.Delay(100);
        expected=before.Resolve(layer.Bounds.Size,panel.Bounds.Size);
        if(Math.Abs(Canvas.GetLeft(panel)-expected.X)>1||Math.Abs(Canvas.GetTop(panel)-expected.Y)>1)throw new InvalidOperationException("Output resize lost profiler anchoring.");
        _output.Width=width;_output.Height=height;await Task.Delay(100);
        Console.WriteLine("FLOATING PROFILER UI PASS: original edge/center placement, routed header drag/capture/release, boundary clamp, preference commit and cancelled-drag restore; compositor input is synthetic.");
    }
}
