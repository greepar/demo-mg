using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Magpie.Desktop.Core;

internal sealed record WindowsApplicationIcon(uint Width,uint Height,byte[] Pixels)
{
    private sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public IconHandle():base(true) { }
        protected override bool ReleaseHandle(){Destroy(handle);return true;}
    }
    [DllImport("magpie_native",EntryPoint="mp_windows_window_icon")] private static extern IconHandle ReadWindow(ulong window,uint process,uint thread,uint preferred);
    [DllImport("magpie_native",EntryPoint="mp_windows_executable_icon")] private static extern IconHandle ReadFile([MarshalAs(UnmanagedType.LPUTF8Str)] string path,uint preferred);
    [DllImport("magpie_native",EntryPoint="mp_windows_icon_pixels")] private static extern int ReadPixels(IconHandle icon,out nint pixels,out uint width,out uint height);
    [DllImport("magpie_native",EntryPoint="mp_windows_icon_destroy")] private static extern void Destroy(nint icon);
    internal static WindowsApplicationIcon? Read(WindowsCaptureSelection selected,uint preferred)
    {
        if(!OperatingSystem.IsWindows())return null;
        using var icon=ReadWindow(selected.Source.WindowId,selected.Source.ProcessId,selected.ThreadId,preferred);
        return CopyPixels(icon);
    }
    internal static WindowsApplicationIcon? ReadExecutable(string path,uint preferred)
    {
        if(!OperatingSystem.IsWindows()||string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))return null;
        using var icon=ReadFile(path,preferred);return CopyPixels(icon);
    }
    private static WindowsApplicationIcon? CopyPixels(IconHandle icon)
    {
        if(icon.IsInvalid)return null;
        if(ReadPixels(icon,out var pixels,out uint width,out uint height)!=1||pixels==0||width==0||height==0||
            width>4096||height>4096||(ulong)width*height>1048576)throw new InvalidDataException("Invalid application icon readback.");
        byte[] bytes=new byte[checked((int)(width*height*4))];Marshal.Copy(pixels,bytes,0,bytes.Length);
        return new(width,height,bytes);
    }
}
