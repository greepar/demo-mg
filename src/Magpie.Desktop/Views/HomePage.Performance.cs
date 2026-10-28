using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class HomePage
{
    private bool _refreshingPerformance;
    private double[] _minimumRates=[0,5,10,15,20,30,60];
    private void RefreshPerformanceSettings()
    {
        _refreshingPerformance=true;
        var settings=RuntimeSettings.Current;
        _minimumRates=[0,5,10,15,20,30,60];
        if(!_minimumRates.Contains(settings.MinimumFrameRate))_minimumRates=[.._minimumRates,settings.MinimumFrameRate];
        MinimumFrameRateSelector.ItemsSource=_minimumRates;
        MinimumFrameRateSelector.SelectedIndex=Array.IndexOf(_minimumRates,settings.MinimumFrameRate);
        DuplicateFrameSelector.SelectedIndex=(int)settings.DuplicateFrameDetection;
        DynamicDetectionStatistics.IsChecked=settings.StatisticsForDynamicDetection;
        GpuProfilingToggle.IsChecked=settings.GpuProfilingEnabled;
        DisableFP16Toggle.IsChecked=settings.DisableFP16;
        DisableTopmostToggle.IsChecked=settings.DisableTopmost;
        DisableTopmostToggle.IsEnabled=OperatingSystem.IsWindows();
        KeepScreenOnToggle.IsChecked=settings.KeepScreenOn;
        DynamicDetectionStatistics.IsEnabled=settings.DuplicateFrameDetection==DuplicateFrameMode.Dynamic;
        _refreshingPerformance=false;
    }
    private void PerformanceSetting_Changed(object? sender,SelectionChangedEventArgs args)=>SavePerformanceSettings();
    private void DynamicStatistics_Changed(object? sender,RoutedEventArgs args)=>SavePerformanceSettings();
    private void SavePerformanceSettings()
    {
        if(_refreshingPerformance || KeepScreenOnToggle is null || DisableTopmostToggle is null || DisableFP16Toggle is null || GpuProfilingToggle is null || MinimumFrameRateSelector is null || DynamicDetectionStatistics is null || DuplicateFrameSelector is null || MinimumFrameRateSelector.SelectedIndex<0 || DuplicateFrameSelector.SelectedIndex<0)return;
        var settings=RuntimeSettings.Current;
        var previous=(settings.MinimumFrameRate,settings.DuplicateFrameDetection,settings.StatisticsForDynamicDetection,settings.GpuProfilingEnabled,settings.DisableFP16,settings.KeepScreenOn,settings.DisableTopmost);
        try
        {
            settings.MinimumFrameRate=_minimumRates[MinimumFrameRateSelector.SelectedIndex];
            settings.DuplicateFrameDetection=(DuplicateFrameMode)DuplicateFrameSelector.SelectedIndex;
            settings.StatisticsForDynamicDetection=DynamicDetectionStatistics.IsChecked==true;
            settings.GpuProfilingEnabled=GpuProfilingToggle.IsChecked==true;
            settings.DisableFP16=DisableFP16Toggle.IsChecked==true;
            settings.KeepScreenOn=KeepScreenOnToggle.IsChecked==true;
            settings.DisableTopmost=DisableTopmostToggle.IsChecked==true;
            settings.Save();DynamicDetectionStatistics.IsEnabled=settings.DuplicateFrameDetection==DuplicateFrameMode.Dynamic;
        }
        catch(Exception failure)
        {
            (settings.MinimumFrameRate,settings.DuplicateFrameDetection,settings.StatisticsForDynamicDetection,settings.GpuProfilingEnabled,settings.DisableFP16,settings.KeepScreenOn,settings.DisableTopmost)=previous;
            RefreshPerformanceSettings();if(TopLevel.GetTopLevel(this) is MainWindow owner)owner.ShowToast("Performance settings",failure.Message);
        }
    }
}
