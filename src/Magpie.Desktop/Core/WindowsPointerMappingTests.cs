using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;
internal static class WindowsPointerMappingTests
{
    internal static void Verify()
    {
        void Check(bool ok) {if(!ok)throw new InvalidOperationException("Original Windows physical cursor mapping differs.");}
        Check(Marshal.SizeOf<NativePointerRect>()==16&&Marshal.SizeOf<NativeCursorShape>()==24);
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
