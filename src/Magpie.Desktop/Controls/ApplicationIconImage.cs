using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;

internal static class ApplicationIconImage
{
    internal static WriteableBitmap Create(WindowsApplicationIcon icon)
    {
        if(icon.Width==0||icon.Height==0||icon.Width>4096||icon.Height>4096||(ulong)icon.Width*icon.Height>1048576||
            icon.Pixels is null||(ulong)icon.Pixels.Length!=(ulong)icon.Width*icon.Height*4)throw new InvalidDataException("Invalid application icon pixels.");
        var bitmap=new WriteableBitmap(new PixelSize((int)icon.Width,(int)icon.Height),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Premul);
        try
        {
            using var pixels=bitmap.Lock();
            for(int y=0;y<(int)icon.Height;y++)Marshal.Copy(icon.Pixels,y*(int)icon.Width*4,pixels.Address+y*pixels.RowBytes,(int)icon.Width*4);
            return bitmap;
        }
        catch {bitmap.Dispose();throw;}
    }
}
