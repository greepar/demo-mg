using Avalonia;

namespace Magpie.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--nnedi3-stress")
        {
            try {Core.RuntimeSettings.PersistenceEnabled=false;Core.Nnedi3Stress.Run(int.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture),args[2]);return 0;}
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        if(args.Length==2&&args[0]=="--dds-export-probe")
        {
            try { Core.DdsExportTests.WriteProbe(args[1]);return 0; }
            catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        }
        if(args.Length==1&&args[0]=="--interaction-model-test")
        {
            try { Core.NativeCore.VerifyInteractionModels();return 0; }
            catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        }
        if(args.Length==1&&args[0]=="--configuration-migration-test")
        {
            try { Core.OriginalConfigurationTests.Verify();return 0; }
            catch(Exception error) { Console.Error.WriteLine(error);return 1; }
        }
        if(args.Length==3&&args[0]=="--migrate-original-config")return Core.OriginalConfiguration.RunAsync(args[1],args[2]).GetAwaiter().GetResult();
        if(args.Length==3&&args[0]=="--configuration-probe")return Core.ConfigurationLocationTests.Probe(args[1],args[2]);
        if(args.Length==2&&args[0]=="--configuration-reveal-probe")return Core.ConfigurationReveal.Probe(args[1]);
        // Non-GUI child receipt for the isolated startup command parser test.
        if(args.Length>=2 && args[0]=="--startup-command-probe")
        {
            File.WriteAllLines(args[1],args.Skip(2).Append(Environment.CurrentDirectory));
            File.WriteAllText(args[1]+".ready","");return 0;
        }
        if(args.Length==2 && args[0]=="--global-shortcut-probe")return Core.GlobalShortcutProbe.Run(args[1]);
        if(args.Length==2 && args[0]=="--screen-awake-probe")return Core.ScreenAwakeProbe.Run(args[1]);
        if(args.Length==2 && args[0]=="--localization-probe")return Core.LocalizationProbe.Run(args[1]);
        if (args.Contains("--core-smoke-test"))
        {
            try { return Core.NativeCore.SmokeTest(); }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        Core.RuntimeSettings.PersistenceEnabled = !args.Any(a => a is "--preview" or "--smoke-test" or "--capture" or "--tray-integration-test");
        try { Core.RuntimeSettings.Load(); }
        catch (Exception error) { Console.Error.WriteLine("Settings could not be loaded; using defaults: " + error.Message); }
        if(args.Contains("--tray-disabled"))Core.RuntimeSettings.Current.ShowTrayIcon=false;
        string language=args.Contains("--tray-integration-test") ? "en-US" : Core.RuntimeSettings.Current.Language;
        if(args.Contains("--preview"))
        {
            int languageIndex=Array.IndexOf(args,"--preview-language");
            language=languageIndex>=0 && languageIndex+1<args.Length ? args[languageIndex+1] : "en-US";
            Core.RuntimeSettings.Current.Language=language;
        }
        Core.Localization.Configure(language);
        Controls.PopupAlignment.Install();
        var builder=BuildAvaloniaApp();
        if(args.Contains("--tray-integration-test"))Avalonia.Logging.Logger.Sink=new Core.TrayProbeLog();
        return builder.StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
        // Linux is native Wayland only: do not silently fall back to XWayland.
        builder = OperatingSystem.IsLinux() ? builder.UseWayland().UseSkia().UseHarfBuzz() : builder.UsePlatformDetect();
        return builder.LogToTrace();
    }
}
