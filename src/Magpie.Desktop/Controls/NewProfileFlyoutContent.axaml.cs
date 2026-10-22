using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;
public partial class NewProfileFlyoutContent : UserControl
{
    internal event Action<ApplicationProfile>? Created;
    public NewProfileFlyoutContent() => InitializeComponent();
    internal void RefreshSources()
    {
        CopySource.Items.Clear();CopySource.Items.Add(new ComboBoxItem { Content="Defaults" });
        foreach(var profile in RuntimeSettings.Current.Profiles)CopySource.Items.Add(new ComboBoxItem { Content=profile.Name,Tag=profile.Id });
        CopySource.SelectedIndex=0;
    }
    internal void ShowFormForPreview()
    {
        ExecutableInput.Text=Path.Combine(Path.GetTempPath(),"example-app");NameInput.Text="Example";
    }
    private void Input_Changed(object? sender,TextChangedEventArgs args)
    {
        if(CreateButton is not null)CreateButton.IsEnabled=!string.IsNullOrWhiteSpace(NameInput?.Text) && Path.IsPathFullyQualified(ExecutableInput?.Text ?? "");
    }
    private void Create_Click(object? sender,RoutedEventArgs args)
    {
        try
        {
            var profile=RuntimeSettings.Current.AddProfile(NameInput.Text ?? "",ExecutableInput.Text ?? "",(CopySource.SelectedItem as ComboBoxItem)?.Tag as string);
            ErrorText.IsVisible=false;Created?.Invoke(profile);
        }
        catch(Exception error) { ErrorText.Text=error.Message;ErrorText.IsVisible=true; }
    }
    private async void Browse_Click(object? sender,RoutedEventArgs args)
    {
        if(TopLevel.GetTopLevel(this) is not { } owner)return;
        try
        {
            var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title="Select executable",AllowMultiple=false });
            if(files.Count==0)return;
            using var file=files[0];ExecutableInput.Text=file.TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local executable.");
            if(string.IsNullOrWhiteSpace(NameInput.Text))NameInput.Text=Path.GetFileNameWithoutExtension(ExecutableInput.Text);
        }
        catch(Exception error) { ErrorText.Text=error.Message;ErrorText.IsVisible=true; }
    }
}
