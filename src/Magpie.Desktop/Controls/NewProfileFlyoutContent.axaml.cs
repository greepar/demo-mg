using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;
public partial class NewProfileFlyoutContent : UserControl
{
    internal event Action<ApplicationProfile>? Created;
    private Func<Task<IReadOnlyList<WindowsProfileCandidate>>>? _candidateFixture;
    private WindowsProfileCandidate? _selectedWindow;
    private int _refreshGeneration;
    private bool _loading;
    private bool WindowsPicker=>OperatingSystem.IsWindows()||_candidateFixture is not null;
    public NewProfileFlyoutContent()
    {
        InitializeComponent();InitializeNameMenu();DetachedFromVisualTree+=(_,_)=>{++_refreshGeneration;ClearCandidateImages();};
    }
    internal void SetCandidateFixture(Func<Task<IReadOnlyList<WindowsProfileCandidate>>> reader)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Candidate fixtures require isolated settings.");
        _candidateFixture=reader;
    }
    internal async void RefreshSources()=>await RefreshSourcesAsync();
    internal async Task RefreshSourcesAsync()
    {
        int generation=++_refreshGeneration;
        CopySource.Items.Clear();CopySource.Items.Add(new ComboBoxItem { Content=LocExtension.Get("Root_Defaults.Content") });
        foreach(var profile in RuntimeSettings.Current.Profiles)CopySource.Items.Add(new ComboBoxItem { Content=profile.Name,Tag=profile.Id });
        CopySource.SelectedIndex=0;
        WindowsSources.IsVisible=WindowsPicker;ExecutableFields.IsVisible=!WindowsPicker;ProfileFields.IsVisible=!WindowsPicker;
        ErrorText.IsVisible=false;
        if(!WindowsPicker){UpdateConfirm();return;}
        _loading=true;_selectedWindow=null;CandidateWindows.Items.Clear();ClearCandidateImages();NameInput.Text="";
        CandidatesLoading.IsVisible=true;NoCandidates.IsVisible=false;UpdateConfirm();
        try
        {
            // Generic visual previews never enumerate a real desktop. The
            // hosted fixture supplies an explicitly guarded reader itself.
            uint iconSize=(uint)Math.Clamp(Math.Round(16*(TopLevel.GetTopLevel(this)?.RenderScaling??1)),1,512);
            IReadOnlyList<WindowsProfileCandidate> rows=_candidateFixture is {} fixture?await fixture():
                RuntimeSettings.PersistenceEnabled?await Task.Run(()=>WindowsProfileCandidates.Read(iconSize)):[];
            if(generation!=_refreshGeneration)return;
            var available=WindowsProfileCandidates.Available(rows,RuntimeSettings.Current);
            foreach(var row in available)
                CandidateWindows.Items.Add(new ComboBoxItem {Tag=row,Content=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10,
                    Children={CandidateImage(row.Icon),new TextBlock {Text=row.Title,MaxWidth=190,TextTrimming=TextTrimming.CharacterEllipsis}}}});
            NoCandidates.IsVisible=available.Count==0;ProfileFields.IsVisible=available.Count>0;
            CandidateWindows.SelectedIndex=available.Count==1?0:-1;
        }
        catch(Exception error)
        {
            if(generation==_refreshGeneration){ErrorText.Text=error.Message;ErrorText.IsVisible=true;}
        }
        finally {if(generation==_refreshGeneration){_loading=false;CandidatesLoading.IsVisible=false;UpdateConfirm();}}
    }
    private readonly List<WriteableBitmap> _candidateImages=[];
    private void ClearCandidateImages()
    {
        foreach(var bitmap in _candidateImages)bitmap.Dispose();_candidateImages.Clear();
    }
    private Control CandidateImage(WindowsApplicationIcon? icon)
    {
        if(icon is null)return new SymbolIcon {Kind="Window",Width=16,Height=16};
        var bitmap=new WriteableBitmap(new PixelSize((int)icon.Width,(int)icon.Height),new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Premul);
        using(var pixels=bitmap.Lock())
            for(int y=0;y<(int)icon.Height;y++)Marshal.Copy(icon.Pixels,y*(int)icon.Width*4,pixels.Address+y*pixels.RowBytes,(int)icon.Width*4);
        _candidateImages.Add(bitmap);return new Image {Source=bitmap,Width=16,Height=16};
    }
    private void Refresh_Click(object? sender,RoutedEventArgs args)=>RefreshSources();
    private void Window_SelectionChanged(object? sender,SelectionChangedEventArgs args)
    {
        _selectedWindow=(CandidateWindows.SelectedItem as ComboBoxItem)?.Tag as WindowsProfileCandidate;
        NameInput.Text=_selectedWindow?.DefaultName??"";UpdateConfirm();
    }
    internal void ShowFormForPreview()
    {
        ExecutableInput.Text=Path.Combine(Path.GetTempPath(),"example-app");NameInput.Text="Example";
    }
    private void Input_Changed(object? sender,TextChangedEventArgs args)=>UpdateConfirm();
    private void UpdateConfirm()
    {
        if(CreateButton is not null)CreateButton.IsEnabled=!_loading&&!string.IsNullOrWhiteSpace(NameInput?.Text)&&
            (WindowsPicker?_selectedWindow is not null:Path.IsPathFullyQualified(ExecutableInput?.Text??""));
    }
    private void Create_Click(object? sender,RoutedEventArgs args)
    {
        if(_loading)return;
        try
        {
            string? copy=(CopySource.SelectedItem as ComboBoxItem)?.Tag as string;
            var root=RuntimeSettings.Current;
            var profile=WindowsPicker?root.AddWindowsProfile(NameInput.Text??"",_selectedWindow?.Window??throw new InvalidDataException("Choose an application window."),copy):
                root.AddProfile(NameInput.Text??"",ExecutableInput.Text??"",copy);
            ErrorText.IsVisible=false;Created?.Invoke(profile);
        }
        catch(Exception error) {ErrorText.Text=error.Message;ErrorText.IsVisible=true;}
    }
    private async void Browse_Click(object? sender,RoutedEventArgs args)
    {
        if(TopLevel.GetTopLevel(this) is not { } owner)return;
        try
        {
            var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title="Select executable",AllowMultiple=false });
            if(files.Count==0)return;
            using var file=files[0];ExecutableInput.Text=file.TryGetLocalPath()??throw new InvalidOperationException("Choose a local executable.");
            if(string.IsNullOrWhiteSpace(NameInput.Text))NameInput.Text=Path.GetFileNameWithoutExtension(ExecutableInput.Text);
        }
        catch(Exception error) {ErrorText.Text=error.Message;ErrorText.IsVisible=true;}
    }
}
