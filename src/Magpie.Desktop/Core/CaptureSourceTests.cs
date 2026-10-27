using System.Runtime.InteropServices;
using System.Text;
namespace Magpie.Desktop.Core;

internal static class CaptureSourceTests
{
    internal static unsafe void Verify()
    {
        if(Marshal.SizeOf<NativeCaptureSource>()!=5400 || Marshal.OffsetOf<NativeCaptureSource>(nameof(NativeCaptureSource.WindowId))!=(nint)16 || Marshal.OffsetOf<NativeCaptureSource>(nameof(NativeCaptureSource.Title))!=(nint)24)
            throw new InvalidOperationException("Capture metadata ABI differs from the native layout.");
        NativeCaptureSource native=default;native.Version=1;native.Kind=1;native.Flags=31;native.ProcessId=42;native.WindowId=314;
        Encoding.UTF8.GetBytes("窗口/😀").CopyTo(new Span<byte>(native.Title,1024));Encoding.UTF8.GetBytes("source-app").CopyTo(new Span<byte>(native.ProcessName,256));
        Encoding.UTF8.GetBytes("/apps/source-app").CopyTo(new Span<byte>(native.ExecutablePath,4096));
        var source=CaptureSource.Decode(native);
        if(source.Title!="窗口/😀"||source.ProcessName!="source-app"||source.ExecutablePath!="/apps/source-app"||source.ProcessId!=42||source.WindowId!=314)throw new InvalidOperationException("Source metadata UTF-8 fields did not cross the managed ABI.");
        if(ScreenshotNames.Format("%WT%_%PN%",source.ScreenshotTitle,source.ScreenshotProcess,DateTime.Now)!="窗口_😀_source-app.png")throw new InvalidOperationException("Screenshot did not use sanitized source title/process metadata.");
        native.Flags=0;source=CaptureSource.Decode(native);
        if(source.Title.Length!=0||source.ProcessName.Length!=0||source.ExecutablePath.Length!=0||source.ProcessId!=0||source.WindowId!=0||source.ScreenshotProcess=="Magpie")throw new InvalidOperationException("Unflagged metadata was used as a real source identity.");
        native.Version=99;
        try { CaptureSource.Decode(native);throw new Exception("Unknown metadata version accepted."); }catch(InvalidDataException) { }
        native.Version=1;native.Flags=1;new Span<byte>(native.Title,1024).Fill(65);
        try { CaptureSource.Decode(native);throw new Exception("Unterminated metadata accepted."); }catch(InvalidDataException) { }
        using var capture=NativeCore.CaptureCreate();if(capture.IsInvalid)throw NativeCore.Error();
        if(NativeCore.CaptureSourceState(capture)!=0)throw new InvalidOperationException("Unselected native source availability ABI differs.");
        if(NativeCore.CaptureSourceInfo(capture,out native,5400)==0)throw NativeCore.Error();
        source=CaptureSource.Decode(native);
        if(source!=CaptureSource.Unknown)throw new InvalidOperationException("An unselected capture fabricated source identity.");
        if(NativeCore.CaptureSourceInfo(capture,out _,1)!=0)throw new InvalidOperationException("Source metadata accepted an incompatible caller buffer.");
        Console.WriteLine("SOURCE METADATA PASS: native/managed layout, actual unselected capture ABI, UTF-8 title/process/path, truthful unknown fields, sanitized screenshot tokens, bad version/termination/size rejection; no capture picker.");
    }
}
