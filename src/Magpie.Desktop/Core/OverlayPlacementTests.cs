using Avalonia;
using System.Text.Json;

namespace Magpie.Desktop.Core;

internal static class OverlayPlacementTests
{
    internal static void Verify()
    {
        static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException("Profiler placement: "+message); }
        var viewport=new Size(1000,800);var panel=new Size(310,400);
        Check(new OverlayPlacement().Resolve(viewport,panel)==new Point(630,200),"Original right-edge/vertical-center default differs.");
        foreach(var point in new[]{new Point(0,0),new(10,15),new(345,200),new(690,400),new(660,380)})
        {
            var saved=OverlayPlacement.FromPosition(point,viewport,panel);
            Check(Math.Abs(saved.Resolve(viewport,panel).X-point.X)<1e-8&&Math.Abs(saved.Resolve(viewport,panel).Y-point.Y)<1e-8,"User drag did not round-trip through original edge/center rules.");
        }
        Check(OverlayPlacement.FromPosition(new(10,15),viewport,panel).HArea==0,"Near-left placement lost edge affinity.");
        Check(OverlayPlacement.FromPosition(new(345,200),viewport,panel).HArea==1,"Center placement lost proportional affinity.");
        var right=OverlayPlacement.FromPosition(new(660,380),viewport,panel);
        Check(right.HArea==2&&right.Resolve(new Size(1200,900),panel)==new Point(860,480),"Resize did not retain right/bottom distances.");
        Check(new OverlayPlacement().Resolve(new Size(200,100),panel)==default(Point),"Oversized panel escaped viewport.");
        foreach(var bad in new[]{new OverlayPlacement(-1),new(1,1,1.01,.5),new(2,1,double.NaN,.5),new(2,1,60,double.PositiveInfinity)})
        {
            try { bad.Validate();throw new InvalidOperationException("Invalid position accepted."); }catch(InvalidDataException) { }
        }
        var settings=new RuntimeSettings { ProfilerPlacement=right };
        var restored=JsonSerializer.Deserialize(JsonSerializer.Serialize(settings,SettingsJson.Default.RuntimeSettings),SettingsJson.Default.RuntimeSettings)!;
        Check(restored.ProfilerPlacement==right,"AOT settings roundtrip lost panel placement.");
        var copy=settings.CopyForProfile();copy.ProfilerPlacement=new();Check(settings.ProfilerPlacement==right,"Copied profile mutated original placement.");
        var imported=OriginalConfiguration.Parse(OriginalConfigurationTests.Example.Replace("\"stats\"","\"profiler\""));
        Check(imported.Settings.ProfilerPlacement==new OverlayPlacement(1,2,.3,.7),"Original overlay window placement was not migrated.");
        Check(!imported.Warnings.Any(w=>w.StartsWith("overlay.windows:",StringComparison.Ordinal)),"Applied profiler placement is still reported unsupported.");
        Console.WriteLine("PROFILER PLACEMENT MODEL PASS: original default/edge/center/resize affinity, drag roundtrip, small viewport, malformed values, AOT preference/copy and original overlay migration.");
    }
}
