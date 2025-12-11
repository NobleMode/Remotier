using System.Windows;
using Remotier.Services;

namespace Remotier;

public partial class SettingWindow : Window
{
    private HostService _hostService;

    public SettingWindow(HostService hostService)
    {
        InitializeComponent();
        _hostService = hostService;

        // Init State
        // In a real app we'd bind to a ViewModel or Settings object. 
        // usage assumption: HostService manages runtime state of port mapping?
        // Actually HostService doesn't yet have PortMapping exposed nicely.
        // I will add a method to HostService to check status or subscribe/bind.

        // For now, assume default is off or check service?
        // Let's rely on HostService to manage the active state.

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        // Placeholder until HostService integration is complete
        bool isEnabled = _hostService.IsPortMappingEnabled;
        UpnpToggle.IsChecked = isEnabled;
        UpnpStatus.Text = _hostService.PortMappingStatus;
    }

    private async void UpnpToggle_Click(object sender, RoutedEventArgs e)
    {
        bool enable = UpnpToggle.IsChecked == true;
        UpnpStatus.Text = enable ? "Initializing..." : "Disabling...";

        await _hostService.TogglePortMapping(enable);

        UpnpStatus.Text = _hostService.PortMappingStatus;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

