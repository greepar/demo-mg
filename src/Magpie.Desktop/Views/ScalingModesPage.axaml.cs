using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Magpie.Desktop.Controls;
namespace Magpie.Desktop.Views;
public partial class ScalingModesPage : UserControl
{
    public Control? ActiveConfirmation { get; private set; }
    public ScalingModesPage()
    {
        InitializeComponent();
        RefreshCustomModes();
        NewModeForm.Created += _ => { RefreshCustomModes(); NewModeForm.RefreshChoices(); NewScalingModeButton.Flyout?.Hide(); };
        int[] sliderParameters=[0,1,2,4,5,6,7,8,9,10,11,12,13,14];
        for(int i=0;i<sliderParameters.Length;i++)
        {
            int slot=sliderParameters[i];var slider=this.FindControl<Slider>("EffectParameter"+(i+2))!;
            slider.Value=Core.RuntimeSettings.Current.Crt[slot];
            var definition=Core.CrtParameters.Definitions[slot];slider.IsSnapToTickEnabled=definition.Integer;slider.TickFrequency=definition.Integer ? 1 : .01;
            slider.PropertyChanged+=(_,args)=> { if(args.Property!=Slider.ValueProperty)return;Core.RuntimeSettings.Current.Crt[slot]=(float)(definition.Integer ? Math.Round(slider.Value) : slider.Value);Core.RuntimeSettings.Current.Save(); };
        }
        CrtCurvature.IsChecked=Core.RuntimeSettings.Current.Crt[3]!=0;
        CrtCurvature.IsCheckedChanged+=(_,_)=> { Core.RuntimeSettings.Current.Crt[3]=CrtCurvature.IsChecked==true?1:0;Core.RuntimeSettings.Current.Save(); };
        LanczosScale.ConnectLanczosSettings();
        EffectParameter1.Value = Core.RuntimeSettings.Current.FsrSharpness;
        EffectParameter1.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty) return;
            Core.RuntimeSettings.Current.FsrSharpness = (float)EffectParameter1.Value;
            Core.RuntimeSettings.Current.Save();
        };
        EffectParameter0.Value = Core.RuntimeSettings.Current.LanczosAntiRinging;
        EffectParameter0.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty) return;
            Core.RuntimeSettings.Current.LanczosAntiRinging = (float)EffectParameter0.Value;
            Core.RuntimeSettings.Current.Save();
        };
    }
    private void UseCrt_Click(object? sender,RoutedEventArgs args)
    {
        Core.RuntimeSettings.Current.SelectedModeId=null;Core.RuntimeSettings.Current.Effect=12;Core.RuntimeSettings.Current.Save();
    }
    private void UseCunny_Click(object? sender,RoutedEventArgs args)
    {
        Core.RuntimeSettings.Current.SelectedModeId=null;Core.RuntimeSettings.Current.Effect=11;Core.RuntimeSettings.Current.Save();
    }
    private void UseFsrcnnx_Click(object? sender,RoutedEventArgs args)
    {
        Core.RuntimeSettings.Current.SelectedModeId=null;Core.RuntimeSettings.Current.Effect=9;Core.RuntimeSettings.Current.Save();
    }
    private void UseAnime4K_Click(object? sender,RoutedEventArgs args)
    {
        Core.RuntimeSettings.Current.SelectedModeId=null;Core.RuntimeSettings.Current.Effect=8;Core.RuntimeSettings.Current.Save();
    }
    private void UseFsr_Click(object? sender, RoutedEventArgs args)
    {
        Core.RuntimeSettings.Current.SelectedModeId = null;
        Core.RuntimeSettings.Current.Effect = 6;
        Core.RuntimeSettings.Current.Save();
    }
    private void RefreshCustomModes()
    {
        CustomModes.Children.Clear();
        foreach (var mode in Core.RuntimeSettings.Current.Modes) CustomModes.Children.Add(new EffectModeEditor(mode, RefreshCustomModes));
    }
    private void DeleteMode_Click(object? sender, RoutedEventArgs args)
    {
        if (sender is not MenuItem { Tag: string name }) return;
        var expander = this.GetVisualDescendants().OfType<SettingsExpander>().First(e => e.Header as string == name);
        var anchor = expander.Action is StackPanel panel ? panel.Children.OfType<Button>().Last() : (Control)expander;
        var content = new ScalingModeDeleteFlyoutContent();
        ActiveConfirmation = content;
        content.ShowLinkedProfiles(name == "Lanczos");
        var flyout = new Flyout { Content = content };
        PopupAlignment.Track(anchor, flyout);
        content.Confirmed += (_, _) => flyout.Hide();
        Dispatcher.UIThread.Post(() => flyout.ShowAt(anchor));
    }
}
