namespace Magpie.Desktop.Core;

internal sealed class ConfigurationLocation
{
    internal const string FileName="desktop-settings.json";
    internal string UserPath { get; }
    internal string PortablePath { get; }
    internal bool Portable { get; private set; }
    internal string Path=>Portable?PortablePath:UserPath;
    private readonly Action<string> _delete;
    internal ConfigurationLocation(string userPath,string portablePath,Action<string>? delete=null)
    {
        UserPath=System.IO.Path.GetFullPath(userPath);PortablePath=System.IO.Path.GetFullPath(portablePath);
        if(UserPath.Equals(PortablePath,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidDataException("Portable and user configuration paths must differ.");
        Portable=File.Exists(PortablePath);_delete=delete??File.Delete;
    }
    internal static string ApplicationDirectory(string baseDirectory,bool macOS)
    {
        var directory=new DirectoryInfo(System.IO.Path.GetFullPath(baseDirectory));
        // Keep mutable preferences outside a signed app bundle.
        if(macOS && directory.Name=="MacOS" && directory.Parent?.Name=="Contents" && directory.Parent.Parent is {} bundle && bundle.Name.EndsWith(".app",StringComparison.OrdinalIgnoreCase))
            return bundle.Parent?.FullName ?? throw new InvalidDataException("Application bundle has no parent directory.");
        return directory.FullName;
    }
    internal static ConfigurationLocation Create()=>new(
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Magpie",FileName),
        System.IO.Path.Combine(ApplicationDirectory(AppContext.BaseDirectory,OperatingSystem.IsMacOS()),"config",FileName));
    internal void SetPortable(RuntimeSettings settings,bool value)
    {
        if(value==Portable)return;
        string destination=value?PortablePath:UserPath;
        // Commit the complete current preferences before changing active location.
        // On disabling, deleting the portable file is the startup-mode commit.
        // If it fails, keep using the portable file and report the failure.
        settings.WriteFile(destination);
        if(!value)_delete(PortablePath);
        Portable=value;
    }
}
