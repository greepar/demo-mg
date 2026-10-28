using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;
internal static class WindowsPointerMappingTests
{
    internal static void Verify()
    {
        void Check(bool ok) {if(!ok)throw new InvalidOperationException("Original Windows physical cursor mapping differs.");}
        Check(Marshal.SizeOf<NativePointerRect>()==16&&Marshal.SizeOf<NativeCursorShape>()==24);
        Check(WindowsCursorVisibility.Draw(1,false,0,0)&&WindowsCursorVisibility.Draw(2,true,1,2));
        Check(!WindowsCursorVisibility.Draw(0,true,1,2)&&!WindowsCursorVisibility.Draw(2,true,2,2)&&
            !WindowsCursorVisibility.Draw(2,false,1,2)&&!WindowsCursorVisibility.Draw(2,true,1,1));
        var m=new WindowsPointerMapping(new(-20,-30,80,20),new(100,200,400,350),new(80,180,450,380));
        Check(m.ToOutput(-20,-30)==(100,200)&&m.ToOutput(79,19)==(399,349));
        Check(m.ToSource(100,200)==(-20,-30)&&m.ToSource(399,349)==(79,19));
        Check(m.ToOutput(30,-5)==(251,276)&&m.ToSource(251,276)==(30,-5));
        Check(m.ToOutput(80,20)==(400,350)&&m.ToOutput(80,20,true)==(450,380));
        Check(m.ToOutput(-21,-31)==(99,199)&&m.ToOutput(-21,-31,true)==(79,179));
        Check(m.ToSource(402,353)==(82,23)&&m.ToSource(98,197)==(-22,-33));
        Check(WindowsPointerMapping.Axis(1,0,3,0,4,0,4)==2);
        Check(WindowsPointerMapping.Axis(4,4,5,-9,-7,-9,-7)==-9);
        bool rejected=false;try {WindowsPointerMapping.Axis(0,0,0,0,1,0,1);}catch(ArgumentException){rejected=true;}Check(rejected);
        var screen=new NativePointerRect(0,0,1000,1000);
        var full=new WindowsPointerMapping(new(100,100,200,200),screen,screen);
        NativePointerRect? Clip(NativePointerRect[] monitors,bool captured=true,bool focused=true)=>WindowsMonitorClip.Compute(full,monitors,150,150,captured,focused);
        Check(Clip([screen])!.Value.Equals(full.Source));
        Check(Clip([screen,new(1000,0,2000,1000)])!.Value.Equals(new NativePointerRect(100,100,int.MaxValue,200)));
        Check(Clip([screen,new(-1000,0,0,1000)])!.Value.Equals(new NativePointerRect(int.MinValue,100,200,200)));
        Check(Clip([screen,new(0,-1000,1000,0)])!.Value.Equals(new NativePointerRect(100,int.MinValue,200,200)));
        Check(Clip([screen,new(0,1000,1000,2000)])!.Value.Equals(new NativePointerRect(100,100,200,int.MaxValue)));
        Check(Clip([screen,new(1200,0,2200,1000)])!.Value.Right==int.MaxValue);
        Check(Clip([screen,new(1000,1100,2000,2100)])!.Value.Right==200);
        Check(Clip([screen,new(1000,505,2000,800)])!.Value.Right==int.MaxValue);
        Check(Clip([screen,new(1000,100,2000,505)])!.Value.Right==200);
        Check(Clip([]) is null&&Clip([screen],false,false) is null);
        var bars=new WindowsPointerMapping(full.Source,new(100,100,900,900),screen);
        Check(WindowsMonitorClip.Compute(bars,[screen],150,150,true,true)!.Value.Equals(full.Source));
        Check(WindowsMonitorClip.Compute(bars,[screen],150,150,true,false)!.Value.Equals(new NativePointerRect(0,0,300,300)));
        Check(WindowsMonitorClip.Compute(bars,[screen],500,500,false,true)!.Value.Equals(bars.Destination));
        var cropped=new WindowsPointerMapping(full.Source,new(-5,-7,1005,1007),screen);
        Check(WindowsMonitorClip.Compute(cropped,[screen],150,150,true,true)!.Value.Equals(new NativePointerRect(101,101,198,198)));
        if(!OperatingSystem.IsWindows())
        {
            Check(NativeCore.WindowsSystemCursor(0)==0&&NativeCore.WindowsPointerWindow(0,0,0,0)==0);
            Check(NativeCore.WindowsCursorCopy(0,out _,24)==0);
            using var capture=NativeCore.CaptureCreate();
            Check(!capture.IsInvalid&&NativeCore.CaptureWindowsPointerRect(capture,1,1,1,out _,16)==0);
        }
        Console.WriteLine("WINDOWS POINTER MAPPING PASS: original pixel centres, both endpoints, unscaled exits, black-bar skip, negative desktop origins, half rounding and unsupported platform ABI; native input/cursor delivery is a separate hosted test.");
    }
}
