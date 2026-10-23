using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct StageSnapshot
{
    public ulong Serial;
    public uint Index,Count,Effect,Width,Height,Reserved;
    internal static StageSnapshot[] Read(EngineHandle engine)
    {
        if(Marshal.SizeOf<StageSnapshot>()!=32)throw new InvalidDataException("Stage snapshot ABI size differs.");
        if(NativeCore.StageSnapshotInfo(engine,0,out var first,32)==0)throw NativeCore.Error();
        if(first.Serial==0||first.Count is <1 or >17)throw new InvalidDataException("Invalid stage snapshot manifest.");
        var result=new StageSnapshot[first.Count];result[0]=first;
        for(uint i=1;i<first.Count;i++)
            if(NativeCore.StageSnapshotInfo(engine,i,out result[i],32)==0)throw NativeCore.Error();
        for(uint i=0;i<first.Count;i++)
            if(result[i].Serial!=first.Serial||result[i].Count!=first.Count||result[i].Index!=i||result[i].Width==0||result[i].Height==0||
                (ulong)result[i].Width*result[i].Height>16777216||result[i].Reserved!=0)throw new InvalidDataException("Stage snapshots changed during manifest query.");
        return result;
    }
    internal unsafe byte[] Copy(EngineHandle engine)
    {
        if(Width==0||Height==0||(ulong)Width*Height>16777216)throw new InvalidDataException("Invalid stage output dimensions.");
        var pixels=new byte[checked((int)((ulong)Width*Height*4))];
        fixed(byte* target=pixels)if(NativeCore.CopyStageSnapshot(engine,Serial,Index,target,(ulong)pixels.Length)==0)throw NativeCore.Error();
        return pixels;
    }
}
