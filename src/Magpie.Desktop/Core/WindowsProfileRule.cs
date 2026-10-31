namespace Magpie.Desktop.Core;

// A reviewed native window rule is not an import archive or a launch command.
internal sealed class WindowsProfileRule
{
    public bool Packaged {get;set;}
    public string PathOrAumid {get;set;}="";
    public string WindowClass {get;set;}="";
    internal WindowsWindowIdentity Identity=>new(Packaged,PathOrAumid,WindowClass);
    internal void Validate()
    {
        if(string.IsNullOrWhiteSpace(PathOrAumid)||PathOrAumid.Length>4096||PathOrAumid.Contains('\0')||
            WindowClass is null||WindowClass.Length>1024||WindowClass.Contains('\0'))throw new InvalidDataException("Invalid Windows application window rule.");
    }
}
