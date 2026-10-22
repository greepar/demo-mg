using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    private sealed record DeviceChoice(string Identifier,string Label)
    {
        public override string ToString()=>Label;
    }
    private VulkanDevice[] _devices=[];
    private bool _devicesLoaded,_loadingDevices,_refreshingDeviceChoices;
    private string? _deviceError;
    private Task? _deviceLoadTask;
    internal Task ReloadGraphicsDevicesAsync()=>_deviceLoadTask is { IsCompleted:false } pending ? pending : _deviceLoadTask=ReloadDevicesAsync();
    private async void RefreshGraphicsDevices_Click(object? sender,RoutedEventArgs args)=>await ReloadGraphicsDevicesAsync();
    private async Task ReloadDevicesAsync()
    {
        if(_loadingDevices)return;
        _loadingDevices=true;RefreshGraphicsDevices.IsEnabled=false;GraphicsDeviceSelector.IsEnabled=false;
        GraphicsDeviceStatus.Text="Checking available Vulkan devices…";
        try
        {
            _devices=await Task.Run(VulkanDevices.Enumerate);
            _devicesLoaded=true;_deviceError=null;
        }
        catch(Exception failure) { _deviceError=failure.Message; }
        finally
        {
            _loadingDevices=false;RefreshGraphicsDevices.IsEnabled=true;GraphicsDeviceSelector.IsEnabled=true;RefreshDeviceChoices();
        }
    }
    private void RefreshDeviceChoices()
    {
        _refreshingDeviceChoices=true;
        try
        {
            var selected=Settings.GraphicsDeviceId;
            var choices=new List<DeviceChoice> { new("",Localization.Text("Automatic")) };
            foreach(var device in _devices)
            {
                string label=device.Label;
                if(_devices.Count(d=>d.Name==device.Name)>1)label+=" · "+device.Identifier[..8];
                choices.Add(new(device.Identifier,label));
            }
            int index=choices.FindIndex(c=>c.Identifier.Equals(selected,StringComparison.OrdinalIgnoreCase));
            bool missing=index<0;
            if(missing) { choices.Add(new(selected,"Unavailable · "+selected[..8]));index=choices.Count-1; }
            GraphicsDeviceSelector.ItemsSource=choices;GraphicsDeviceSelector.SelectedIndex=index;
            GraphicsDeviceStatus.Text=_deviceError is {} failure ? "Device query failed: "+failure
                : _loadingDevices ? "Checking available Vulkan devices…"
                : missing && _devicesLoaded ? "The selected device is unavailable. Choose another device or Automatic before starting."
                : !_devicesLoaded ? "Device list has not been loaded."
                : _devices.Length==0 ? "No usable Vulkan 1.1 device is available."
                : "Applies to the next scaling session.";
            GraphicsCard.Description=selected.Length==0 ? "Vulkan device selected automatically" : _devices.FirstOrDefault(d=>d.Identifier.Equals(selected,StringComparison.OrdinalIgnoreCase))?.Name ?? "Selected Vulkan device";
        }
        finally { _refreshingDeviceChoices=false; }
    }
    private void GraphicsDevice_SelectionChanged(object? sender,SelectionChangedEventArgs args)
    {
        if(!_initialized || _refreshingDeviceChoices || GraphicsDeviceSelector.SelectedItem is not DeviceChoice choice)return;
        string previous=Settings.GraphicsDeviceId;
        Settings.GraphicsDeviceId=choice.Identifier;
        try { RuntimeSettings.Current.Save();_deviceError=null; }
        catch(Exception failure) { Settings.GraphicsDeviceId=previous;_deviceError="Could not save selection: "+failure.Message; }
        RefreshDeviceChoices();
    }
}
