using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Remotier.Models;
using System.Windows.Input;
using Remotier.Services;
using Remotier.Services.Network;

namespace Remotier
{
    public partial class ConnectWindow : Window
    {
        private ObservableCollection<string> _recentConnections;
        private DiscoveryService _discoveryService;

        public ConnectWindow()
        {
            InitializeComponent();
            LoadRecents();

            _discoveryService = new DiscoveryService();
            _discoveryService.StartListening();
            DiscoveredList.ItemsSource = _discoveryService.DiscoveredHosts;
        }

        private void LoadRecents()
        {
            var recents = ConfigService.LoadRecentConnections();
            _recentConnections = new ObservableCollection<string>(recents);
            RecentList.ItemsSource = _recentConnections;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            string input = IpInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(input)) return;

            string ip = input;
            int port = 5000;

            int colonIndex = input.LastIndexOf(':');
            if (colonIndex != -1)
            {
                ip = input.Substring(0, colonIndex);
                string portStr = input.Substring(colonIndex + 1);
                if (!int.TryParse(portStr, out port))
                {
                    MessageBox.Show("Invalid port number.");
                    return;
                }
            }

            // Save to recents
            ConfigService.SaveConnection(input);

            var info = new ConnectionInfo
            {
                IP = ip,
                Port = port,
                AccountName = AccountNameInput.Text.Trim(),
                Password = ShowPasswordToggle.IsChecked == true ? PasswordVisibleInput.Text : PasswordInput.Password
            };

            // Verify Connection BEFORE opening the window
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                var clientService = new ClientService();
                await clientService.ConnectAsync(info.IP, info.Port, info.AccountName, info.Password);

                // If we get here, Auth Success
                var remoteView = new RemoteViewWindow(info, clientService);
                remoteView.Closed += (s, args) => this.Close();
                remoteView.Show();
                this.Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection Failed: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecentList.SelectedItem is string selectedIp)
            {
                IpInput.Text = selectedIp;
            }
        }

        private void DiscoveredList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DiscoveredList.SelectedItem is DiscoveredHost host)
            {
                IpInput.Text = $"{host.IP}:{host.Port}";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _discoveryService?.Stop();
            base.OnClosed(e);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowPassword_Checked(object sender, RoutedEventArgs e)
        {
            PasswordVisibleInput.Text = PasswordInput.Password;
            PasswordVisibleInput.Visibility = Visibility.Visible;
            PasswordInput.Visibility = Visibility.Collapsed;
        }

        private void ShowPassword_Unchecked(object sender, RoutedEventArgs e)
        {
            PasswordInput.Password = PasswordVisibleInput.Text;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordVisibleInput.Visibility = Visibility.Collapsed;
        }
    }
}
