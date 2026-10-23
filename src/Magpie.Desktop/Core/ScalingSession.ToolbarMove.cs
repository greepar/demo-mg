using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private Action<Window,PointerPressedEventArgs>? _previewMoveDrag;

    private void TryMoveFromToolbar(PointerPressedEventArgs args)
    {
        if(args.Handled || args.Properties.PointerUpdateKind!=PointerUpdateKind.LeftButtonPressed ||
            _output is not {} output || output.WindowState is WindowState.FullScreen or WindowState.Minimized ||
            _toolbar is not { IsVisible:true,IsHitTestVisible:true } toolbar || toolbar.Opacity<=0 ||
            args.Source is not Visual source || !IsToolbarCaption(source,toolbar) ||
            !new Rect(toolbar.Bounds.Size).Contains(args.GetPosition(toolbar)))return;
        try
        {
            // Preserve the actual pointer event/cookie. Wayland window moves
            // must be requested through the compositor with its input serial;
            // updating Window.Position cannot implement interactive dragging.
            if(_previewMoveDrag is {} preview)
            {
                if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Move fixtures require isolated preview mode.");
                preview(output,args);
            }
            else output.BeginMoveDrag(args);
            args.Handled=true;
        }
        catch(Exception error)
        {
            string message=(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"无法移动窗口：":"Window move failed: ")+error.Message;
            Status?.Invoke(message);
            if(_message is not null) { _message.Text=message;_message.IsVisible=true; }
        }
        // The compositor owns the pointer after a successful native handoff;
        // Wayland may not deliver the mouse-up back to this surface. This flag
        // protects local control gestures only and must not get stuck true.
        finally { _toolbarDragging=false; }
    }

    private static bool IsToolbarCaption(Visual source,Border toolbar)
    {
        for(Visual? item=source;item is not null;item=item.GetVisualParent())
        {
            if(ReferenceEquals(item,toolbar))return true;
            // Include control templates: a click on a button label or combo
            // chevron belongs to that control, not to the window caption.
            if(item is Button or SelectingItemsControl or TextBox or NumericUpDown or RangeBase or Thumb or Spinner)return false;
        }
        return false;
    }
}
