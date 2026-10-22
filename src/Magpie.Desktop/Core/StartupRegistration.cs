using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Magpie.Desktop.Core;

internal interface IStartupRegistration
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
}

internal static class StartupRegistration
{
    internal const string Id="io.github.magpie.Desktop";
    internal const string Marker="X-Magpie-Managed=true";
    internal static string DesktopString(string value)
    {
        Validate(value);
        return value.Replace("\\","\\\\").Replace(" ","\\s");
    }
    private static void Validate(string value)
    {
        if(value.Any(char.IsControl))throw new InvalidDataException("Startup paths and arguments cannot contain control characters.");
    }
    internal static string DesktopArgument(string value)
    {
        Validate(value);
        string quoted="\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("`","\\`").Replace("$","\\$").Replace("%","%%")+"\"";
        return DesktopString(quoted);
    }
    internal static string WindowsArgument(string value)
    {
        Validate(value);
        var result=new StringBuilder("\"");int slashes=0;
        foreach(char c in value)
        {
            if(c=='\\') { slashes++;continue; }
            result.Append('\\',c=='"' ? slashes*2+1 : slashes);result.Append(c);slashes=0;
        }
        return result.Append('\\',slashes*2).Append('"').ToString();
    }
    internal static string DesktopEntry(string[] command,string directory,bool hidden=false)
    {
        // GIO checks the executable before expanding %% in Exec's first token.
        // env keeps a literal percent in the executable path in an argument token.
        string[] exec=command;
        if(command[0].Contains('%'))
        {
            string launcher=File.Exists("/usr/bin/env") ? "/usr/bin/env" : File.Exists("/bin/env") ? "/bin/env" : throw new IOException("A system env executable is required for startup paths containing %. ");
            exec=[launcher,"--",..command];
        }
        return "[Desktop Entry]\nType=Application\nName=Magpie\n"+Marker+"\nTerminal=false\nHidden="+(hidden?"true":"false")+
            "\nExec="+string.Join(" ",exec.Select(DesktopArgument))+"\nTryExec="+DesktopString(command[0])+"\nPath="+DesktopString(directory)+"\n";
    }
    internal static string LaunchAgent(string[] command,string directory)
    {
        foreach(string argument in command)Validate(argument);Validate(directory);
        return new XDocument(new XElement("plist",new XAttribute("version","1.0"),new XElement("dict",
            new XElement("key","Label"),new XElement("string",Id),
            new XElement("key","ProgramArguments"),new XElement("array",command.Select(a=>new XElement("string",a))),
            new XElement("key","WorkingDirectory"),new XElement("string",directory),
            new XElement("key","RunAtLoad"),new XElement("true")))).ToString();
    }
    internal static IStartupRegistration Create()
    {
        var start=ApplicationRestart.CurrentStartInfo([]);
        string[] previousCommand=[start.FileName,..start.ArgumentList];
        string[] command=[..previousCommand,"--autostart"];
        string directory=Path.GetDirectoryName(previousCommand.Length>1 ? previousCommand[1] : previousCommand[0])!;
        string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(OperatingSystem.IsWindows())return new WindowsStartupRegistration(string.Join(" ",command.Select(WindowsArgument)),string.Join(" ",previousCommand.Select(WindowsArgument)));
        if(OperatingSystem.IsMacOS())
            return new FileStartupRegistration(Path.Combine(home,"Library","LaunchAgents",Id+".plist"),LaunchAgent(command,directory),null,[],"<string>"+Id+"</string>",LaunchAgent(previousCommand,directory));
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException("Startup registration is unavailable on this platform.");
        string? config=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if(string.IsNullOrEmpty(config)||!Path.IsPathFullyQualified(config))config=Path.Combine(home,".config");
        string[] system=(Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS") is { Length:>0 } dirs ? dirs : "/etc/xdg")
            .Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries).Where(Path.IsPathFullyQualified)
            .Select(d=>Path.Combine(d,"autostart",Id+".desktop")).ToArray();
        return new FileStartupRegistration(Path.Combine(config,"autostart",Id+".desktop"),DesktopEntry(command,directory),DesktopEntry(command,directory,true),system,Marker,DesktopEntry(previousCommand,directory));
    }
}

// File operations are atomic and scoped to this application's own registration.
// Injecting paths lets tests exercise the same code without touching the user's login setup.
internal sealed class FileStartupRegistration(string path,string enabledContent,string? disabledContent,string[] systemPaths,string marker,string? previousEnabledContent=null) : IStartupRegistration
{
    private string? Read(string file)
    {
        if(new FileInfo(file).LinkTarget is not null)throw new IOException("Startup registration is a symbolic link: "+file);
        if(!File.Exists(file))return null;
        string content=File.ReadAllText(file);
        if(!content.Contains(marker,StringComparison.Ordinal))throw new IOException("An unrelated startup entry already uses this filename: "+file);
        return content;
    }
    public bool Enabled
    {
        get
        {
            string? value=Read(path);
            if(value is null)
            {
                foreach(string system in systemPaths)if((value=Read(system)) is not null)break;
            }
            if(value is null||value==disabledContent||disabledContent is not null && value.Split('\n').Any(line=>line.Trim()=="Hidden=true"))return false;
            if(value!=enabledContent&&value!=previousEnabledContent)throw new IOException("Startup registration differs from this installation. Enable it again after removing the obsolete entry: "+path);
            return true;
        }
    }
    public void SetEnabled(bool enabled)
    {
        _=Read(path);
        bool inherited=systemPaths.Any(File.Exists);
        string? content=enabled ? enabledContent : inherited ? disabledContent : null;
        if(content is null) { File.Delete(path);return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,content,new UTF8Encoding(false));File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsStartupRegistration(string command,string? previousCommand=null) : IStartupRegistration
{
    private const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="Magpie.Desktop";
    public bool Enabled
    {
        get
        {
            using var key=Registry.CurrentUser.OpenSubKey(KeyPath);
            object? value=key?.GetValue(Name,null,RegistryValueOptions.DoNotExpandEnvironmentNames);
            if(value is null)return false;
            if(value is not string text||text!=command&&text!=previousCommand)throw new IOException("The Magpie.Desktop startup entry belongs to another installation.");
            return true;
        }
    }
    public void SetEnabled(bool enabled)
    {
        _=Enabled;
        if(enabled&&command.Length>260)throw new IOException("Windows startup command exceeds 260 characters. Move Magpie to a shorter path.");
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath,true);
        if(enabled)key.SetValue(Name,command,RegistryValueKind.String);else key.DeleteValue(Name,false);
    }
}
