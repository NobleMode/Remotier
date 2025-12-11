using System.Windows;
using Remotier.Services;

namespace Remotier;

public partial class SettingWindow : Window
{
    private HostService? _hostService;
    public Remotier.Models.SecuritySettings SecuritySettings { get; private set; }

    public SettingWindow(HostService? hostService = null)
    {
        InitializeComponent();
        _hostService = hostService;
        SecuritySettings = Remotier.Models.SecuritySettings.Load();

        // Account Tab
        AccountNameBox.Text = SecuritySettings.AccountName;
        UpdatePasswordStatus();

        // Security Tab
        RefreshLists();

        // Network Tab
        UpdateStatus();
    }

    private void UpdatePasswordStatus()
    {
        if (!string.IsNullOrEmpty(SecuritySettings.AccountPasswordHash))
        {
            PasswordStatusText.Text = "Status: Password Configured (Hidden)";
            PasswordStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green
        }
        else
        {
            PasswordStatusText.Text = "Status: No Password Set (Guest Access Only)";
            PasswordStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
        }
    }

    private void RefreshLists()
    {
        TrustedDevicesList.ItemsSource = null;
        TrustedDevicesList.ItemsSource = SecuritySettings.TrustedDevices;

        RecentConnectionsList.ItemsSource = null;
        RecentConnectionsList.ItemsSource = SecuritySettings.RecentConnections;
    }

    private void SaveAccount_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SecuritySettings.AccountName = AccountNameBox.Text;

            // Get password from visible box
            string password = ShowPasswordToggle.IsChecked == true ? AccountPasswordVisibleBox.Text : AccountPasswordBox.Password;

            if (!string.IsNullOrEmpty(password))
            {
                // Hash it
                SecuritySettings.AccountPasswordHash = HostService.ComputeSha256(password);
            }
            // Logic change: If empty, do we clear it? User implies "fail" if it's not showing up.
            // If they clear it, they probably want to remove it?
            // Let's assume empty means "No Change" if it was already set, OR "Remove" if they explicitly cleared it?
            // Standard: Empty = No Change. 
            // BUT, if they want to clear it?
            // Let's stick to Empty = No Change for now to avoid accidental clears.

            SecuritySettings.Save();

            // Reload Host Settings if active
            _hostService?.ReloadSettings();

            UpdatePasswordStatus();
            MessageBox.Show("Account settings saved successfully.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowPassword_Checked(object sender, RoutedEventArgs e)
    {
        AccountPasswordVisibleBox.Text = AccountPasswordBox.Password;
        AccountPasswordVisibleBox.Visibility = Visibility.Visible;
        AccountPasswordBox.Visibility = Visibility.Collapsed;
    }

    private void ShowPassword_Unchecked(object sender, RoutedEventArgs e)
    {
        AccountPasswordBox.Password = AccountPasswordVisibleBox.Text;
        AccountPasswordBox.Visibility = Visibility.Visible;
        AccountPasswordVisibleBox.Visibility = Visibility.Collapsed;
    }

    private void RemoveTrusted_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Remotier.Models.DeviceInfo device)
        {
            SecuritySettings.TrustedDevices.Remove(device);
            SecuritySettings.Save();
            RefreshLists();
        }
    }

    private void UpdateStatus()
    {
        if (_hostService == null)
        {
            UpnpToggle.IsEnabled = false;
            UpnpStatus.Text = "Not Hosting";
            return;
        }

        bool isEnabled = _hostService.IsPortMappingEnabled;
        UpnpToggle.IsChecked = isEnabled;
        UpnpStatus.Text = _hostService.PortMappingStatus;
    }

    private async void UpnpToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_hostService == null) return;

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

