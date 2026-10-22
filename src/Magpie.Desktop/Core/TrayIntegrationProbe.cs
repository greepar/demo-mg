using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Magpie.Desktop.Views;
namespace Magpie.Desktop.Core;

internal sealed record TrayProbeState(bool Ready,bool Visible,bool Hidden,bool Enabled,int Opened,bool Exited,string Ack,string[] Starts,string? Error);
[JsonSerializable(typeof(TrayProbeState))]
internal partial class TrayProbeJson : JsonSerializerContext { }
internal static class TrayIntegrationProbe
{
    private static readonly List<string> Starts=[];
    internal static void RecordStart(bool fullscreen,int seconds)=>Starts.Add((fullscreen?"fullscreen:":"windowed:")+seconds);
    // Explicit CLI fixture: no persistence and no capture. The runner provides a
    // private D-Bus plus headless Wayland; menu starts are recorded, never captured.
    internal static void Start(MainWindow window,IClassicDesktopStyleApplicationLifetime desktop,string receipt)
    {
        if(Environment.GetEnvironmentVariable("MAGPIE_TRAY_PRIVATE_SESSION")!="1")throw new InvalidOperationException("Tray integration probes require the isolated test runner.");
        var integration=window.DesktopIntegration ?? throw new InvalidOperationException("Tray controller missing.");
        int opened=0;bool exited=false;string ack="";string? error=null;
        window.Opened+=(_,_)=>opened++;
        void Write()
        {
            var state=new TrayProbeState(integration.IsReady,window.IsVisible,integration.IsHidden,RuntimeSettings.Current.ShowTrayIcon,opened,exited,ack,Starts.ToArray(),error);
            File.WriteAllText(receipt+".tmp",JsonSerializer.Serialize(state,TrayProbeJson.Default.TrayProbeState));File.Move(receipt+".tmp",receipt,true);
        }
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(50) };
        timer.Tick+=(_,_)=>
        {
            try
            {
                string commandPath=receipt+".command";
                if(File.Exists(commandPath))
                {
                    string[] command=File.ReadAllLines(commandPath);
                    if(command.Length==2&&command[0]!=ack)
                    {
                        ack=command[0];
                        Console.WriteLine("TRAY PROBE command: "+command[1]);
                        switch(command[1])
                        {
                            case "close":window.Close();break;
                            case "minimize":window.WindowState=WindowState.Minimized;break;
                            case "disable":case "enable":
                                window.Navigate("Settings");
                                ((SettingsPage)window.FindControl<ContentControl>("ContentFrame")!.Content!).FindControl<ToggleSwitch>("TrayToggle")!.IsChecked=command[1]=="enable";break;
                            case "delay5":
                                window.Navigate("Home");((HomePage)window.FindControl<ContentControl>("ContentFrame")!.Content!).FindControl<Slider>("DelaySeconds")!.Value=5;break;
                            case "show":integration.Show();break;
                            case "screenshot":
                                var control=window.FindControl<Border>("WindowBorder")!;
                                using(var bitmap=new RenderTargetBitmap(Avalonia.PixelSize.FromSize(control.Bounds.Size,1))) { bitmap.Render(control);bitmap.Save(receipt+".png",PngBitmapEncoderOptions.Default); }break;
                            default:throw new InvalidDataException("Unknown tray probe command.");
                        }
                        Console.WriteLine("TRAY PROBE command completed: "+command[1]);
                    }
                }
                if(!exited)Write();
            }
            catch(Exception failure) { error=failure.ToString();Write();desktop.Shutdown(1); }
        };
        desktop.Exit+=(_,_)=> { exited=true;timer.Stop();Write(); };
        timer.Start();Write();
    }
}
