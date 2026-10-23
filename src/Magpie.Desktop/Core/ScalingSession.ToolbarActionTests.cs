using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static async Task VerifyToolbarActionsAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Toolbar fixtures require isolated preview mode.");
        foreach(bool fullscreen in new[]{false,true})
        {
            var settings=new RuntimeSettings { FullscreenInitialToolbarState=ToolbarState.AutoHide,WindowedInitialToolbarState=ToolbarState.AutoHide };
            var session=new ScalingSession(settings);int frames=0,finished=0;string? failure=null;
            session.Finished+=error=> { finished++;failure=error; };
            session._previewLoop=async stop=>
            {
                await Dispatcher.UIThread.InvokeAsync(()=>session.OpenOutput(owner,fullscreen));
                while(!stop.IsCancellationRequested) { Interlocked.Increment(ref frames);await Task.Delay(10,stop); }
            };
            var run=session.RunAsync(owner,fullscreen,0);
            try
            {
                for(int attempt=0;session._output is null&&attempt<100;attempt++)await Task.Delay(20);
                if(session._output is not {} output)throw new InvalidOperationException("Toolbar output did not open.");
                await Task.Delay(100);
                var toolbar=session._toolbar!;var pin=session._toolbarPin!;
                var close=output.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="StopScalingButton");
                var toggle=ControlAutomationPeer.CreatePeerForElement(pin)?.GetProvider<IToggleProvider>() ?? throw new InvalidOperationException("Pin has no accessible toggle action.");
                session._toolbarPointer=new Point(20,20);session.UpdateToolbarVisibility();
                toggle.Toggle();session._toolbarPointer=null;session.UpdateToolbarVisibility();
                if(session._toolbarState!=ToolbarState.AlwaysShow||pin.IsChecked!=true||toolbar.Opacity!=1)throw new InvalidOperationException("Pin did not keep the toolbar visible away from the pointer.");
                toggle.Toggle();pin.Focus(NavigationMethod.Pointer);session.UpdateToolbarVisibility();
                if(session._toolbarState!=ToolbarState.AutoHide||pin.IsChecked!=false||toolbar.Opacity!=0)throw new InvalidOperationException("Mouse focus prevented unpinned toolbar from hiding.");
                output.FocusManager!.Focus(null);
                session._toolbarPointer=new Point(20,20);session.UpdateToolbarVisibility();pin.Focus(NavigationMethod.Tab);session._toolbarPointer=null;session.UpdateToolbarVisibility();
                if(toolbar.Opacity!=1)throw new InvalidOperationException("Keyboard focus lost toolbar visibility protection.");
                output.FocusManager!.Focus(null);session.UpdateToolbarVisibility();
                if(toolbar.Opacity!=0)throw new InvalidOperationException("Toolbar did not release keyboard visibility protection.");
                session.ToggleToolbar();session.ToggleToolbar();
                if(pin.IsChecked!=true)throw new InvalidOperationException("Shortcut did not synchronize the pin button.");
                await Task.Delay(80);
                int before=Volatile.Read(ref frames);
                var pointer=new Pointer(27,PointerType.Mouse,true);
                // PointerEventArgs expects presentation-root coordinates. In
                // Avalonia 12 the Window is nested inside a TopLevelHost, so
                // window/client coordinates omit the frame and caption offset.
                var eventRoot=close.GetVisualAncestors().Last();
                var origin=close.TranslatePoint(default,eventRoot) ?? throw new InvalidOperationException("Close button has no output position.");
                var rightPress=new PointerPressedEventArgs(close,pointer,eventRoot,origin+new Vector(3,3),0,new PointerPointProperties(RawInputModifiers.RightMouseButton,PointerUpdateKind.RightButtonPressed),KeyModifiers.None,1);
                close.RaiseEvent(rightPress);
                if(!session._toolbarDragging||!toolbar.IsVisible||session._stop.IsCancellationRequested)throw new InvalidOperationException($"Toolbar did not track routed press: drag={session._toolbarDragging}, bounds={toolbar.Bounds}, point={rightPress.GetPosition(toolbar.Parent as Visual)}, closePoint={rightPress.GetPosition(close)}, route={rightPress.Route}, ancestors={string.Join('/',close.GetVisualAncestors().Select(v=>v.GetType().Name))}.");
                var rightClick=new PointerReleasedEventArgs(close,pointer,eventRoot,origin+new Vector(3,3),0,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.RightButtonReleased),KeyModifiers.None,MouseButton.Right);
                close.RaiseEvent(rightClick);
                await Task.Delay(80);
                if(!rightClick.Handled||session._toolbarState!=ToolbarState.Off||toolbar.IsVisible||toolbar.IsHitTestVisible||session._toolbarDragging||session._stop.IsCancellationRequested||Volatile.Read(ref frames)<=before||finished!=0)throw new InvalidOperationException($"Right-click close failed: handled={rightClick.Handled}, state={session._toolbarState}, visible={toolbar.IsVisible}, hit={toolbar.IsHitTestVisible}, stopped={session._stop.IsCancellationRequested}, frames={before}->{frames}, finished={finished}, bounds={close.Bounds}, point={rightClick.GetPosition(close)}, routes={InputElement.PointerReleasedEvent.RoutingStrategies}.");
                session.ToggleToolbar();
                if(session._toolbarState!=ToolbarState.AlwaysShow||pin.IsChecked!=true)throw new InvalidOperationException("Shortcut did not restore the hidden toolbar.");
                byte[] pixels=new byte[32*24*4];for(int i=0;i<pixels.Length;i++)pixels[i]=i%4==3?(byte)255:(byte)((i*13)%251);
                session.Present(pixels,32,24);
                if(session._statistics is not null)session._statistics.Text="Pin/unpin · right-click hides only · shortcut restores · left-click stops";
                await Task.Delay(80);
                var args=Environment.GetCommandLineArgs();int capture=Array.IndexOf(args,"--capture");
                if(capture>=0&&capture+1<args.Length)
                {
                    using var bitmap=new RenderTargetBitmap(PixelSize.FromSize(output.ClientSize,1));bitmap.Render((Control)output.Content!);
                    bitmap.Save(Path.ChangeExtension(args[capture+1],fullscreen?"fullscreen-output.png":"windowed-output.png"),PngBitmapEncoderOptions.Default);
                }
                var invoke=ControlAutomationPeer.CreatePeerForElement(close)?.GetProvider<IInvokeProvider>() ?? throw new InvalidOperationException("Close has no accessible button action.");
                invoke.Invoke();await run.WaitAsync(TimeSpan.FromSeconds(8));
                if(finished!=1||failure is not null||session._output is not null||session._toolbarTimer is not null)throw new InvalidOperationException("Left-click close did not stop and release its session exactly once.");
                if(settings.FullscreenInitialToolbarState!=ToolbarState.AutoHide||settings.WindowedInitialToolbarState!=ToolbarState.AutoHide)throw new InvalidOperationException("Toolbar actions changed initial preferences.");
            }
            finally
            {
                if(!run.IsCompleted)session.Stop();
                await run.WaitAsync(TimeSpan.FromSeconds(8));
            }
        }
        Console.WriteLine("TOOLBAR ACTIONS PASS: real windowed/fullscreen output, accessible pin/close handlers, pointer versus keyboard focus, routed right-click hides while synthetic frames continue, shortcut/pin synchronization, unchanged preferences and stop/cleanup exactly once; no capture picker.");
    }
}
