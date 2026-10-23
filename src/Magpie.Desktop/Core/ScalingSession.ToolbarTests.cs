using Avalonia;
using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal static async Task VerifyToolbarAsync(MainWindow owner)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Toolbar fixtures require isolated preview mode.");
        foreach(bool fullscreen in new[]{false,true})
        foreach(var initial in Enum.GetValues<ToolbarState>())
        {
            var session=new ScalingSession(new RuntimeSettings { FullscreenInitialToolbarState=initial,WindowedInitialToolbarState=initial });
            try
            {
                session.OpenOutput(owner,fullscreen,800,450);await Task.Delay(100);
                if(session._toolbarState!=initial)throw new InvalidOperationException("Output ignored its initial toolbar preference.");
                var toolbar=session._toolbar!;
                if(initial==ToolbarState.Off && toolbar.IsVisible)throw new InvalidOperationException("Off toolbar remained visible.");
                session._toolbarState=ToolbarState.AutoHide;session._toolbarPointer=null;session.UpdateToolbarVisibility();
                if(toolbar.Opacity!=0 || toolbar.IsHitTestVisible)throw new InvalidOperationException("Hidden toolbar intercepted source input.");
                session._toolbarPointer=new Point(toolbar.Bounds.Left+5,toolbar.Bounds.Bottom);session.UpdateToolbarVisibility();
                if(toolbar.Opacity!=1 || !toolbar.IsHitTestVisible)throw new InvalidOperationException("Pointer proximity did not reveal the toolbar.");
                session._toolbarPointer=null;session._toolbarDragging=true;session.UpdateToolbarVisibility();
                if(toolbar.Opacity!=1)throw new InvalidOperationException("Toolbar hid while dragging its controls.");
                session._toolbarDragging=false;await Task.Delay(100);
                if(toolbar.Opacity!=0)throw new InvalidOperationException("Toolbar timer did not hide after interaction finished.");
                session.ToggleToolbar();if(session._toolbarState!=ToolbarState.Off||toolbar.IsVisible)throw new InvalidOperationException("Auto-hide shortcut did not select Off.");
                session.ToggleToolbar();if(session._toolbarState!=ToolbarState.AlwaysShow||toolbar.Opacity!=1)throw new InvalidOperationException("Off shortcut did not select AlwaysShow.");
                session.ToggleToolbar();if(session._toolbarState!=ToolbarState.AutoHide)throw new InvalidOperationException("AlwaysShow shortcut did not select AutoHide.");
            }
            finally
            {
                session._output?.Close();session._output=null;session._stop.Dispose();session._finished=true;
                if(session._toolbarTimer is not null)throw new InvalidOperationException("Closed output leaked its toolbar timer.");
            }
        }
        Console.WriteLine("TOOLBAR OUTPUT PASS: all six mode/state combinations, actual output opacity/hit testing, active-drag protection, timer hide, shortcut cycle and closed-window cleanup; pointer positions are injected fixture data.");
    }
}
