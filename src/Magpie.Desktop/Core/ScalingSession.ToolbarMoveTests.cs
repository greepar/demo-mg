using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static async Task VerifyToolbarMoveAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Move fixtures require isolated preview mode.");
        foreach(bool fullscreen in new[]{false,true})
        {
            var session=new ScalingSession(new RuntimeSettings { FullscreenInitialToolbarState=ToolbarState.AlwaysShow,WindowedInitialToolbarState=ToolbarState.AlwaysShow });
            int moves=0;bool fail=false;PointerPressedEventArgs? delivered=null;Window? destination=null;
            session._previewMoveDrag=(window,args)=> { if(fail)throw new NotSupportedException("Test compositor rejected window move");moves++;destination=window;delivered=args; };
            try
            {
                session.OpenOutput(owner,fullscreen);await Task.Delay(120);
                var output=session._output!;var toolbar=session._toolbar!;
                PointerPressedEventArgs Press(Control source,bool right=false,bool handled=false)
                {
                    var root=source.GetVisualAncestors().Last();
                    var point=source.TranslatePoint(new Point(2,2),root) ?? throw new InvalidOperationException("Move source has no presentation root.");
                    var args=new PointerPressedEventArgs(source,new Pointer(31,PointerType.Mouse,true),root,point,0,
                        new PointerPointProperties(right?RawInputModifiers.RightMouseButton:RawInputModifiers.LeftMouseButton,right?PointerUpdateKind.RightButtonPressed:PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1) { Handled=handled };
                    source.RaiseEvent(args);return args;
                }
                var background=Press(toolbar);
                if(moves!=(fullscreen?0:1)||(!fullscreen&&(!background.Handled||session._toolbarDragging||!ReferenceEquals(delivered,background)||!ReferenceEquals(destination,output))))throw new InvalidOperationException("Caption did not hand the exact original event to the window move backend, left a local drag pending, or fullscreen started a move.");
                // Release the fixture's pointer activity without entering an OS
                // move loop. Native move/resize lifecycle remains a target test.
                session._toolbarDragging=false;
                int expected=moves;
                Press(toolbar,right:true);Press(toolbar,handled:true);
                Press(session._image!);
                foreach(var control in toolbar.GetVisualDescendants().OfType<Control>().Where(c=>c.Focusable || c is NumericUpDown).ToArray())
                {
                    if(IsToolbarCaption(control,toolbar))throw new InvalidOperationException("Interactive toolbar control became a caption target.");
                    // A control may change selection/focus when pressed. Mark
                    // its event handled as its native class handler would.
                    Press(control,handled:true);
                    var label=control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
                    if(label is not null&&IsToolbarCaption(label,toolbar))throw new InvalidOperationException("Nested control label became a caption target.");
                }
                if(moves!=expected)throw new InvalidOperationException("A control, image, secondary button or handled event moved the output.");
                session.SetToolbarState(ToolbarState.Off);Press(toolbar);
                if(moves!=expected)throw new InvalidOperationException("Off toolbar moved the output.");
                session.SetToolbarState(ToolbarState.AutoHide);session._toolbarPointer=null;session._toolbarDragging=false;session.UpdateToolbarVisibility();
                Press(toolbar);
                if(moves!=expected)throw new InvalidOperationException("Transparent toolbar moved the output.");
                session.SetToolbarState(ToolbarState.AlwaysShow);
                if(!fullscreen)
                {
                    fail=true;string? status=null;session.Status+=message=>status=message;
                    Press(toolbar);
                    if(moves!=expected||session._toolbarDragging||session._message is not { IsVisible:true }||status is null||!status.Contains("Test compositor",StringComparison.Ordinal))throw new InvalidOperationException("Move rejection did not clear drag state and report failure.");
                    fail=false;Press(toolbar);
                    if(moves!=expected+1)throw new InvalidOperationException("A rejected move prevented a later retry.");
                }
            }
            finally
            {
                session._previewMoveDrag=null;session._output?.Close();session._output=null;session._bitmap?.Dispose();session._bitmap=null;session._finished=true;session._stop.Dispose();
                if(session._toolbarTimer is not null)throw new InvalidOperationException("Move fixture leaked its toolbar timer.");
            }
        }
        Console.WriteLine("TOOLBAR MOVE PASS: actual routed caption events, original event identity/native handoff, fullscreen/controls/nested labels/handled/secondary/hidden/transparent exclusions, rejection feedback/retry and closed-window cleanup; compositor move invocation is a test fixture, no physical drag tested.");
    }
}
