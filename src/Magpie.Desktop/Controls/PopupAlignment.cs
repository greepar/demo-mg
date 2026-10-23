using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using System.Runtime.CompilerServices;
namespace Magpie.Desktop.Controls;

// WinUI flyouts are constrained to their XamlRoot by default. Compute the same
// boundary before showing the native popup, rather than moving it after rendering.
internal static class PopupAlignment
{
    private sealed record DropDownLimit(ComboBox Combo,double OriginalHeight);
    private static readonly ConditionalWeakTable<Popup,DropDownLimit> DropDownLimits=new();
    private sealed class MenuPosition { public Window? Owner;public Point Origin; }
    private sealed record MenuPlacement(MenuPosition Parent,MenuPosition Child);
    private static readonly ConditionalWeakTable<MenuItem,MenuPlacement> MenuPlacements=new();
    private static void RegisterMenus(IEnumerable<object?> items,MenuPosition parent)
    {
        foreach(var item in items.OfType<MenuItem>())
        {
            var child=new MenuPosition();MenuPlacements.Remove(item);MenuPlacements.Add(item,new(parent,child));
            RegisterMenus(item.Items,child);
        }
    }
    public static void Install()
    {
        Button.FlyoutProperty.Changed.AddClassHandler<Button>((button, _) =>
        {
            if (button.Flyout is PopupFlyoutBase flyout) Track(button, flyout);
        });
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
        {
            if(!popup.IsOpen)
            {
                if(DropDownLimits.TryGetValue(popup,out var previous))
                {
                    previous.Combo.SetCurrentValue(ComboBox.MaxDropDownHeightProperty,previous.OriginalHeight);DropDownLimits.Remove(popup);
                }
                return;
            }
            var menuItem=popup.PlacementTarget as MenuItem??popup.TemplatedParent as MenuItem;
            if(menuItem is not null&&MenuPlacements.TryGetValue(menuItem,out var menu))
            {
                popup.CustomPopupPlacementCallback=placement=>
                {
                    if(menu.Parent.Owner is not {} owner)return;
                    var rect=placement.AnchorRectangle;var size=placement.PopupSize;var origin=menu.Parent.Origin;
                    double right=origin.X+rect.Right+placement.Offset.X;
                    double left=origin.X+rect.Left-size.Width-placement.Offset.X;
                    double x=right+size.Width<=owner.Bounds.Width-2?right:left>=2?left:right;
                    x=Math.Clamp(x,2,Math.Max(2,owner.Bounds.Width-size.Width-2));
                    double y=Math.Clamp(origin.Y+rect.Top+placement.Offset.Y,2,Math.Max(2,owner.Bounds.Height-size.Height-2));
                    menu.Child.Owner=owner;menu.Child.Origin=new(x,y);
                    placement.AnchorRectangle=new Rect(new Point(x-origin.X,y-origin.Y),new Size(1,1));
                    placement.Anchor=PopupAnchor.TopLeft;placement.Gravity=PopupGravity.BottomRight;placement.Offset=default;
                    placement.ConstraintAdjustment=PopupPositionerConstraintAdjustment.None;
                };
                popup.Placement=PlacementMode.Custom;return;
            }
            var combo=popup.PlacementTarget as ComboBox ?? popup.TemplatedParent as ComboBox;
            if(combo is null || TopLevel.GetTopLevel(combo) is not Window owner)return;
            if(!DropDownLimits.TryGetValue(popup,out var limit)) { limit=new(combo,combo.MaxDropDownHeight);DropDownLimits.Add(popup,limit); }
            var transform=combo.TransformToVisual(owner)??Matrix.Identity;
            double scale=Math.Max(.01,Math.Sqrt(transform.M21*transform.M21+transform.M22*transform.M22));
            combo.SetCurrentValue(ComboBox.MaxDropDownHeightProperty,Math.Min(limit.OriginalHeight,Math.Max(40,(owner.Bounds.Height-32)/scale)));
            if(popup.Placement==PlacementMode.Custom)return;
            var original=popup.Placement;
            popup.CustomPopupPlacementCallback=placement=> { if(TopLevel.GetTopLevel(combo) is Window current)Constrain(current,original,placement); };
            popup.Placement=PlacementMode.Custom;
        });
    }

    public static void Track(Control anchor, PopupFlyoutBase flyout)
    {
        if (flyout.Placement == PlacementMode.Custom) return;
        var originalPlacement = flyout.Placement;
        var menuPosition=new MenuPosition();
        if(flyout is MenuFlyout menu)menu.Opened+=(_,_)=>RegisterMenus(menu.Items,menuPosition);
        flyout.Placement = PlacementMode.Custom;
        flyout.CustomPopupPlacementCallback = placement =>
        {
            if (TopLevel.GetTopLevel(anchor) is not Window owner) return;
            Constrain(owner,originalPlacement,placement);
            menuPosition.Owner=owner;menuPosition.Origin=placement.AnchorRectangle.Position;
        };
    }

    private static void Constrain(Window owner,PlacementMode originalPlacement,CustomPopupPlacement placement)
    {
            var rect = placement.AnchorRectangle;
            var size = placement.PopupSize;
            var point = originalPlacement switch
            {
                PlacementMode.Right => new Point(rect.Right, rect.Center.Y - size.Height / 2),
                PlacementMode.Left => new Point(rect.Left - size.Width, rect.Center.Y - size.Height / 2),
                PlacementMode.Top => new Point(rect.Center.X - size.Width / 2, rect.Top - size.Height),
                PlacementMode.Center => new Point(rect.Center.X - size.Width / 2, rect.Center.Y - size.Height / 2),
                PlacementMode.BottomEdgeAlignedLeft => rect.BottomLeft,
                PlacementMode.BottomEdgeAlignedRight => new Point(rect.Right - size.Width, rect.Bottom),
                _ => new Point(rect.Center.X - size.Width / 2, rect.Bottom)
            };
            var x = Math.Clamp(point.X + placement.Offset.X, 2, Math.Max(2, owner.Bounds.Width - size.Width - 2));
            var y = Math.Clamp(point.Y + placement.Offset.Y, 2, Math.Max(2, owner.Bounds.Height - size.Height - 2));
            placement.AnchorRectangle = new Rect(new Point(x, y), new Size(1, 1));
            placement.Anchor = PopupAnchor.TopLeft;
            placement.Gravity = PopupGravity.BottomRight;
            placement.Offset = default;
            placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.None;
    }
}
