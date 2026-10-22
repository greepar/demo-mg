using Avalonia.Controls;
namespace Magpie.Desktop.Controls;
public partial class NewScalingModeFlyoutContent : UserControl
{
    internal event Action<Core.EffectModeSettings>? Created;
    public NewScalingModeFlyoutContent()
    {
        InitializeComponent();
        RefreshChoices();
    }
    internal void RefreshChoices()
    {
        CopyFrom.ItemsSource = null;
        CopyFrom.Items.Clear();
        CopyFrom.ItemsSource = new[] { Core.Localization.Text("None"), "Lanczos" }.Concat(Core.RuntimeSettings.Current.Modes.Select(m => m.Name)).ToArray();
        CopyFrom.SelectedIndex = 0;
    }
    private void Create_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        string name = NameInput.Text?.Trim() ?? "";
        if (name.Length is 0 or > 128 || Core.RuntimeSettings.Current.Modes.Any(m => m.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
        { ErrorLabel.Text = "Enter a unique name (1–128 characters)."; ErrorLabel.IsVisible = true; return; }
        var mode = CopyFrom.SelectedIndex > 1
            ? Core.RuntimeSettings.Current.Modes[CopyFrom.SelectedIndex-2].Duplicate(name)
            : CopyFrom.SelectedIndex==1 ? Core.ScalingModeInterchange.Builtins(Core.RuntimeSettings.Current)[0].Duplicate(name) : new Core.EffectModeSettings { Name=name,Steps=[] };
        Core.RuntimeSettings.Current.Modes.Add(mode);
        try { Core.RuntimeSettings.Current.Save(); }
        catch (Exception error) { Core.RuntimeSettings.Current.Modes.Remove(mode); ErrorLabel.Text=error.Message; ErrorLabel.IsVisible=true; return; }
        Created?.Invoke(mode);
        NameInput.Text = ""; ErrorLabel.IsVisible = false;
    }
    private void Name_Changed(object? sender, TextChangedEventArgs args)
    {
        if (CreateButton is not null) CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameInput.Text);
    }
}
