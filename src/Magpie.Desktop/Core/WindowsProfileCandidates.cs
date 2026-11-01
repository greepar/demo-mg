using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Magpie.Desktop.Core;

internal sealed record WindowsProfileCandidate(WindowsCaptureSelection Window,string DefaultName,WindowsApplicationIcon? Icon=null)
{
    internal string Title=>Window.Source.Title;
}
internal static class WindowsProfileCandidates
{
    private sealed class ListHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ListHandle():base(true) { }
        protected override bool ReleaseHandle() {Destroy(handle);return true;}
    }
    [DllImport("magpie_native",EntryPoint="mp_windows_candidates_create")] private static extern ListHandle Create();
    [DllImport("magpie_native",EntryPoint="mp_windows_candidates_destroy")] private static extern void Destroy(nint list);
    [DllImport("magpie_native",EntryPoint="mp_windows_candidates_count")] private static extern uint Count(ListHandle list);
    [DllImport("magpie_native",EntryPoint="mp_windows_candidates_get")] private static extern unsafe int Get(ListHandle list,uint index,out NativeCaptureSource source,uint size,byte* windowClass,uint capacity,out uint thread);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] private static extern int CompareStringEx(string? locale,uint flags,string first,int firstLength,string second,int secondLength,nint version,nint reserved,nint parameter);
    internal static unsafe IReadOnlyList<WindowsProfileCandidate> Read(uint preferred=16)
    {
        if(!OperatingSystem.IsWindows())return [];
        using var list=Create();if(list.IsInvalid)throw NativeCore.Error();
        var rows=new List<WindowsProfileCandidate>();byte* windowClass=stackalloc byte[1024];
        for(uint i=0,count=Count(list);i<count;i++)
        {
            if(Get(list,i,out var source,5400,windowClass,1024,out uint thread)!=1)throw NativeCore.Error();
            var selected=WindowsCaptureSelection.Decode(source,new ReadOnlySpan<byte>(windowClass,1024),thread);
            string name=selected.Source.Title;
            try {string? description=FileVersionInfo.GetVersionInfo(selected.Source.ExecutablePath).FileDescription?.Trim();if(!string.IsNullOrEmpty(description))name=description;}
            catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
            rows.Add(new(selected,name,WindowsApplicationIcon.Read(selected,preferred)));
        }
        return rows;
    }
    internal static IReadOnlyList<WindowsProfileCandidate> Available(IEnumerable<WindowsProfileCandidate> rows,RuntimeSettings root)
        =>rows.Where(row=>WindowsProfileMatching.Find(root.Profiles,row.Window.Identity,false) is null)
            .OrderBy(row=>row.Title,Comparer<string>.Create(CompareTitles)).ToArray();
    private static int CompareTitles(string a,string b)
    {
        // Match original SortCandidateWindows, including linguistic casing
        // and the user's Windows locale rather than the app's UI language.
        if(OperatingSystem.IsWindows())
        {
            int order=CompareStringEx(null,0x1|0x20000|0x08000000,a,a.Length,b,b.Length,0,0,0);
            if(order!=0)return order-2;
        }
        return CultureInfo.CurrentCulture.CompareInfo.Compare(a,b,CompareOptions.IgnoreCase|CompareOptions.IgnoreWidth);
    }
}
