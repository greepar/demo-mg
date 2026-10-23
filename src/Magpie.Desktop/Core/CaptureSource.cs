using System.Runtime.InteropServices;
using System.Text;
namespace Magpie.Desktop.Core;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCaptureSource
{
    internal uint Version,Flags,ProcessId,Kind;
    internal ulong WindowId;
    internal fixed byte Title[1024];
    internal fixed byte ProcessName[256];
    internal fixed byte ExecutablePath[4096];
}

internal sealed record CaptureSource(string Title,string ProcessName,string ExecutablePath,uint ProcessId,ulong WindowId,uint Kind)
{
    internal static readonly CaptureSource Unknown=new("","","",0,0,0);
    internal string ScreenshotTitle=>!string.IsNullOrEmpty(Title)?Title:Kind switch
    {
        1=>Chinese?"窗口":"Window",2=>Chinese?"屏幕":"Display",3=>Chinese?"虚拟屏幕":"Virtual display",_=>Chinese?"捕获画面":"Capture"
    };
    internal string ScreenshotProcess=>!string.IsNullOrEmpty(ProcessName)?ProcessName:Chinese?"未知应用":"Unknown";
    private static bool Chinese=>Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
    private static string Text(ReadOnlySpan<byte> bytes)
    {
        int end=bytes.IndexOf((byte)0);
        if(end<0)throw new InvalidDataException("Source metadata string is not terminated.");
        return new UTF8Encoding(false,true).GetString(bytes[..end]);
    }
    internal static unsafe CaptureSource Decode(NativeCaptureSource source)
    {
        if(source.Version!=1||source.Kind>3||(source.Flags&~31u)!=0)throw new InvalidDataException("Unsupported source metadata version, type or flags.");
        return new((source.Flags&1)!=0?Text(new ReadOnlySpan<byte>(source.Title,1024)):"",
            (source.Flags&2)!=0?Text(new ReadOnlySpan<byte>(source.ProcessName,256)):"",
            (source.Flags&4)!=0?Text(new ReadOnlySpan<byte>(source.ExecutablePath,4096)):"",
            (source.Flags&8)!=0?source.ProcessId:0,(source.Flags&16)!=0?source.WindowId:0,source.Kind);
    }
}
