using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Magpie.Desktop.Controls;

public sealed class CountdownRing : Control
{
    public static readonly StyledProperty<double> ValueProperty=AvaloniaProperty.Register<CountdownRing,double>(nameof(Value),1);
    public double Value { get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,value); }
    static CountdownRing()=>AffectsRender<CountdownRing>(ValueProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double radius=Math.Max(0,Math.Min(Bounds.Width,Bounds.Height)/2-2);var center=new Point(Bounds.Width/2,Bounds.Height/2);
        var brush=Brush.Parse("#0067C0");var pen=new Pen(brush,2);
        using(context.PushOpacity(.2))context.DrawEllipse(null,pen,center,radius,radius);
        double value=double.IsFinite(Value)?Math.Clamp(Value,0,1):0;
        if(value<=0||radius<=0)return;
        if(value>=1){context.DrawEllipse(null,pen,center,radius,radius);return;}
        double angle=-Math.PI/2-2*Math.PI*value;
        var geometry=new StreamGeometry();
        using(var path=geometry.Open())
        {
            path.BeginFigure(new Point(center.X,center.Y-radius),false);
            path.ArcTo(new Point(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)),new Size(radius,radius),0,value>.5,SweepDirection.CounterClockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null,pen,geometry);
    }
}
