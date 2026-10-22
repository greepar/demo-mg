using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Magpie.Desktop.Core;
namespace Magpie.Desktop;

public partial class App
{
    private static bool InitializeDesktopIntegration(MainWindow window,IClassicDesktopStyleApplicationLifetime desktop,string[] arguments)
    {
        bool probe=arguments.Contains("--tray-integration-test");
        if(!RuntimeSettings.PersistenceEnabled&&!probe)return false;
        var integration=new DesktopIntegration(window,()=>desktop.Shutdown(),start:probe ? TrayIntegrationProbe.RecordStart : null);
        desktop.Exit+=(_,_)=>integration.Dispose();
        desktop.ShutdownMode=ShutdownMode.OnMainWindowClose;
        if(!arguments.Contains("--autostart")||!RuntimeSettings.Current.ShowTrayIcon)return false;
        // With no MainWindow at lifetime startup, Avalonia cannot flash it before
        // asynchronous StatusNotifier registration has been acknowledged.
        desktop.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async ()=>
        {
            bool hidden=await integration.PrepareHiddenStartupAsync(TimeSpan.FromSeconds(3));
            if(probe)Console.WriteLine("TRAY PROBE startup resolved: hidden="+hidden);
            desktop.MainWindow=window;desktop.ShutdownMode=ShutdownMode.OnMainWindowClose;
            if(!hidden)integration.Show();
            if(probe)Console.WriteLine("TRAY PROBE startup completed.");
        });
        return true;
    }
}
