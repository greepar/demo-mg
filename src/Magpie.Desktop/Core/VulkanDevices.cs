using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Magpie.Desktop.Core;

internal sealed class DeviceListHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DeviceListHandle() : base(true) { }
    protected override bool ReleaseHandle() { NativeCore.DevicesDestroy(handle);return true; }
}
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDeviceInfo
{
    public fixed byte Identifier[33];
    public fixed byte Name[256];
    public uint Type,VendorId,DeviceId;
}
internal sealed record VulkanDevice(string Identifier,string Name,uint Type,uint VendorId,uint DeviceId)
{
    public string Kind=>Type switch { 1=>"Integrated GPU",2=>"Discrete GPU",3=>"Virtual GPU",4=>"Software / CPU",_=>"Other" };
    public string Label=>$"{Name} ({Kind})";
}
internal static class VulkanDevices
{
    public static unsafe VulkanDevice[] Enumerate()
    {
        using var snapshot=NativeCore.DevicesCreate();
        if(snapshot.IsInvalid)throw NativeCore.Error();
        uint count=NativeCore.DevicesCount(snapshot);
        var result=new VulkanDevice[count];
        for(uint i=0;i<count;i++)
        {
            if(NativeCore.DevicesGet(snapshot,i,out var info)==0)throw NativeCore.Error();
            string id=Marshal.PtrToStringUTF8((nint)info.Identifier) ?? "";
            string name=Marshal.PtrToStringUTF8((nint)info.Name) ?? "";
            ValidateIdentifier(id,false);
            result[i]=new(id,name,info.Type,info.VendorId,info.DeviceId);
        }
        if(result.Select(d=>d.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=result.Length)
            throw new InvalidDataException("Vulkan device UUIDs are not unique.");
        return result;
    }
    public static void ValidateIdentifier(string? id,bool allowAutomatic=true)
    {
        if(id is null || !(allowAutomatic && id.Length==0) && (id.Length!=32 || id.Any(c=>!char.IsAsciiHexDigit(c))))
            throw new InvalidDataException("Select Automatic or a Vulkan device UUID with 32 hexadecimal digits.");
    }
}
