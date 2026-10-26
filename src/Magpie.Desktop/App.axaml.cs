using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Controls;

namespace Magpie.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Core.ThemePreference.Apply(this,Core.RuntimeSettings.Current.Theme);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var arguments = desktop.Args ?? [];
            if(!InitializeDesktopIntegration(window,desktop,arguments))desktop.MainWindow=window;
            int trayProbe=Array.IndexOf(arguments,"--tray-integration-test");
            if(trayProbe>=0)Core.TrayIntegrationProbe.Start(window,desktop,arguments[trayProbe+1]);
            if (arguments.Contains("--capture-test")) window.Opened += (_, _) => window.StartScaling(false, 0);
            var previewArgumentIndex = Array.IndexOf(arguments, "--preview");
            var narrow = previewArgumentIndex >= 0 && arguments[previewArgumentIndex + 1].StartsWith("Narrow", StringComparison.Ordinal);
            if (arguments.Contains("--capture") || arguments.Contains("--preview"))
            {
                window.FindControl<LayoutTransformControl>("UiScale")!.LayoutTransform = new Avalonia.Media.ScaleTransform(1.5, 1.5);
                window.Width = narrow ? 750 : 1418;
                window.Height = narrow ? 750 : 1050;
            }
            var pageIndex = Array.IndexOf(arguments, "--page");
            if (pageIndex >= 0 && pageIndex + 1 < arguments.Length) window.Navigate(arguments[pageIndex + 1]);
            if (arguments.Contains("--dark")) RequestedThemeVariant = ThemeVariant.Dark;

            // Exercise the real desktop backend, XAML, text and native graphics before exiting.
            var captureIndex = Array.IndexOf(arguments, "--capture");
            if (arguments.Contains("--smoke-test") || captureIndex >= 0)
            {
                bool previewStarted=false;
                window.Opened += (_, _) => { if(previewStarted)return;previewStarted=true;DispatcherTimer.RunOnce(async () =>
                {
                    try
                    {
                        // Capture the reference viewport even when the desktop work area is shorter.
                        Control captureVisual = window;
                        if (captureIndex >= 0)
                        {
                            captureVisual = window.FindControl<Border>("WindowBorder")!;
                            captureVisual.Width = narrow ? 750 : 1418;
                            captureVisual.Height = narrow ? 750 : 1050;
                            captureVisual.Measure(new Size(captureVisual.Width, captureVisual.Height));
                            captureVisual.Arrange(new Rect(0, 0, captureVisual.Width, captureVisual.Height));
                        }
                        var previewIndex = Array.IndexOf(arguments, "--preview");
                        var popupContents = previewIndex >= 0 && previewIndex + 1 < arguments.Length
                            ? await UiPreview.PrepareAsync(window, arguments[previewIndex + 1]) : [];
                        if (arguments.Contains("--dump-layout"))
                            foreach (var visual in window.GetVisualDescendants().OfType<Control>().Where(v => v.Classes.Contains("key") || v is Controls.ShortcutControl || v is TextBlock))
                                Console.WriteLine($"{visual.GetType().Name} {(visual is TextBlock t ? t.Text + " font=" + t.FontFamily + " weight=" + t.FontWeight : "")} {visual.Bounds} desired={visual.DesiredSize} transformed={visual.GetTransformedBounds()}");
                        var size = PixelSize.FromSize(captureVisual.Bounds.Size, 1);
                        using var bitmap = new RenderTargetBitmap(size);
                        bitmap.Render(captureVisual);
                        if (captureIndex >= 0 && captureIndex + 1 < arguments.Length)
                        {
                            await UiPreview.WaitForPopupFramesAsync(popupContents);
                            UiPreview.Save(captureVisual, window, popupContents, arguments[captureIndex + 1]);
                        }
                        Console.WriteLine($"SMOKE PASS: window opened and rendered ({size.Width}x{size.Height}).");
                        desktop.Shutdown(0);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception);
                        desktop.Shutdown(1);
                    }
                }, TimeSpan.FromSeconds(2)); };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
