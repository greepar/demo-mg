using System.Runtime.InteropServices;
using System.Text;
namespace Magpie.Desktop.Core;

internal sealed record PassExportOutput(string Name,int Texture,uint Format);
internal sealed record PassExportDescription(string Name,PassExportOutput[] Outputs);
internal static partial class PassExportCatalog { }

[StructLayout(LayoutKind.Sequential)]
internal struct PassSnapshot
{
    public ulong Serial,Bytes;
    public uint Stage,Pass,Output,OutputCount,PassCount,Texture,Format,Width,Height,Reserved;
    internal static PassSnapshot Read(EngineHandle engine,uint stage,uint pass,uint output)
    {
        if(Marshal.SizeOf<PassSnapshot>()!=56)throw new InvalidDataException("Pass snapshot ABI differs.");
        if(NativeCore.PassSnapshotInfo(engine,stage,pass,output,out var value,56)==0)throw NativeCore.Error();
        value.Validate();return value;
    }
    internal void Validate()
    {
        if(Serial==0||Stage>=17||PassCount is <1 or >16||Pass>=PassCount||OutputCount is <1 or >32||Output>=OutputCount||
            Width==0||Height==0||Width>16384||Height>16384||Format>5||Reserved!=0||Bytes==0||Bytes>128ul*1024*1024||
            Bytes!=(ulong)Width*Height*DdsExport.BytesPerPixel(Format)||Texture==uint.MaxValue&&(Format!=1||Pass+1!=PassCount))
            throw new InvalidDataException("Invalid pass snapshot metadata.");
    }
    internal unsafe byte[] Copy(EngineHandle engine)
    {
        Validate();var bytes=new byte[checked((int)Bytes)];
        var self=this;
        fixed(byte* target=bytes)
            if(NativeCore.CopyPassSnapshot(engine,self.Serial,self.Stage,self.Pass,self.Output,target,self.Bytes)==0)throw NativeCore.Error();
        return bytes;
    }
}
internal static class DdsExport
{
    private static readonly uint[] PixelBytes=[8,4,2,4,1,2],DxgiFormats=[10,28,54,34,61,49];
    internal static uint BytesPerPixel(uint format)=>format<PixelBytes.Length?PixelBytes[format]:throw new InvalidDataException("Unsupported DDS format.");
    internal static void Write(Stream stream,PassSnapshot info,byte[] pixels)
    {
        info.Validate();if((ulong)pixels.Length!=info.Bytes)throw new InvalidDataException("DDS payload length differs.");
        using var writer=new BinaryWriter(stream,Encoding.UTF8,true);
        writer.Write(0x20534444u);writer.Write(124u);writer.Write(0x100fu);writer.Write(info.Height);writer.Write(info.Width);
        writer.Write(checked(info.Width*BytesPerPixel(info.Format)));writer.Write(0u);writer.Write(0u);
        for(int i=0;i<11;i++)writer.Write(0u);
        writer.Write(32u);writer.Write(4u);writer.Write(0x30315844u);
        for(int i=0;i<5;i++)writer.Write(0u);
        writer.Write(0x1000u);for(int i=0;i<4;i++)writer.Write(0u);
        writer.Write(DxgiFormats[info.Format]);writer.Write(3u);writer.Write(0u);writer.Write(1u);writer.Write(0u);
        writer.Write(pixels);
    }
}
