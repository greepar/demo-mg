using Avalonia;

namespace Magpie.Desktop.Core;

// Original OverlayWindowOption: edge distance in DIPs, or proportional center.
internal sealed record OverlayPlacement(int HArea=2,int VArea=1,double HPos=60,double VPos=.5)
{
    internal void Validate()
    {
        if(HArea is <0 or >2||VArea is <0 or >2||!double.IsFinite(HPos)||!double.IsFinite(VPos)||HPos<0||VPos<0||HPos>(HArea==1?1:100000)||VPos>(VArea==1?1:100000))
            throw new InvalidDataException("Invalid profiler placement.");
    }
    internal Point Resolve(Size viewport,Size panel)
    {
        Validate();
        static double Axis(int area,double position,double available,double size)=>Math.Clamp(area switch {0=>position,1=>available*position-size/2,_=>available-position-size},0,Math.Max(0,available-size));
        return new(Axis(HArea,HPos,viewport.Width,panel.Width),Axis(VArea,VPos,viewport.Height,panel.Height));
    }
    internal static OverlayPlacement FromPosition(Point point,Size viewport,Size panel)
    {
        static (int Area,double Position) Axis(double position,double available,double size)
        {
            if(available<=0||size>=available)return (0,0);
            position=Math.Clamp(position,0,available-size);
            double threshold=Math.Max(size/available,.2),remaining=available-position-size;
            double ratio=remaining>0?position/remaining:double.PositiveInfinity;
            if(ratio<threshold)return (0,position);
            if(ratio<=1/threshold)return (1,(position+size/2)/available);
            return (2,remaining);
        }
        var h=Axis(point.X,viewport.Width,panel.Width);var v=Axis(point.Y,viewport.Height,panel.Height);
        return new(h.Area,v.Area,h.Position,v.Position);
    }
}
