using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Tmds.DBus.Protocol;
namespace Magpie.Desktop.Core;

internal static class ConfigurationReveal
{
    internal static int Probe(string path)
    {
        if(Environment.GetEnvironmentVariable("MAGPIE_CONFIG_PRIVATE_SESSION")!="1")throw new InvalidOperationException("File manager probes require a private bus.");
        try { ShowLinuxAsync(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")!,path).GetAwaiter().GetResult();return 0; }
        catch(Exception error) { Console.Error.WriteLine(error.Message);return 1; }
    }
    internal static ProcessStartInfo NativeCommand(string path,bool windows)
    {
        var command=new ProcessStartInfo(windows?"explorer.exe":"/usr/bin/open") { UseShellExecute=false };
        if(windows)command.ArgumentList.Add("/select,"+path);
        else { command.ArgumentList.Add("-R");command.ArgumentList.Add(path); }
        return command;
    }
    internal static async Task<bool> ShowAsync(TopLevel owner,string path)
    {
        if(!RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("File manager navigation is disabled in preview mode.");
        if(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is {} address)
        {
            try { await ShowLinuxAsync(address,path);return true; }
            catch(Exception error) { Console.Error.WriteLine("Configuration file selection: "+error.Message); }
        }
        else if(OperatingSystem.IsWindows()||OperatingSystem.IsMacOS())
        {
            try
            {
                using var process=Process.Start(NativeCommand(path,OperatingSystem.IsWindows()));
                if(process is not null)
                {
                    // Explorer may retain the process after opening its window.
                    var exited=process.WaitForExitAsync();
                    if(await Task.WhenAny(exited,Task.Delay(1500))!=exited)return true;
                    if(process.ExitCode==0)return true;
                }
            }
            catch(Exception error) { Console.Error.WriteLine("Configuration file selection: "+error.Message); }
        }
        return await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(Path.GetDirectoryName(path)!));
    }
    internal static async Task ShowLinuxAsync(string address,string path)
    {
        using var connection=new DBusConnection(address);
        await connection.ConnectAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await connection.CallMethodAsync(RevealMessage(connection,path)).WaitAsync(TimeSpan.FromSeconds(5));
    }
    private static MessageBuffer RevealMessage(DBusConnection connection,string path)
    {
        using var writer=connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination:"org.freedesktop.FileManager1",path:"/org/freedesktop/FileManager1",@interface:"org.freedesktop.FileManager1",member:"ShowItems",signature:"ass");
        var array=writer.WriteArrayStart(DBusType.String);writer.WriteString(new Uri(Path.GetFullPath(path)).AbsoluteUri);writer.WriteArrayEnd(array);writer.WriteString("");
        return writer.CreateMessage();
    }
}
