using System.Runtime.InteropServices;
using System.Text;
namespace Magpie.Desktop.Core;

internal sealed record WindowsCaptureSelection(CaptureSource Source,uint ThreadId,string WindowClass)
{
    [DllImport("magpie_native",EntryPoint="mp_windows_foreground_source")]
    private static extern unsafe int ForegroundSource(out NativeCaptureSource source,uint size,byte* windowClass,uint capacity,out uint thread);
    internal WindowsWindowIdentity Identity=>new(false,Source.ExecutablePath,WindowClass);
    internal static unsafe WindowsCaptureSelection? Read()
    {
        if(!OperatingSystem.IsWindows())return null;
        byte* bytes=stackalloc byte[1024];
        int result=ForegroundSource(out var native,5400,bytes,1024,out uint thread);
        if(result<0)throw NativeCore.Error();
        if(result==0)return null;
        var source=CaptureSource.Decode(native);var text=new ReadOnlySpan<byte>(bytes,1024);int end=text.IndexOf((byte)0);
        if(source is not {Kind:1,WindowId:>0,ProcessId:>0}||source.ExecutablePath.Length==0||thread==0||end<=0)
            throw new InvalidDataException("Foreground source identity is incomplete.");
        return new(source,thread,new UTF8Encoding(false,true).GetString(text[..end]));
    }
}
