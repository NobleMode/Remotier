using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Remotier.Models;
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

        private void Connect_Click(object sender, RoutedEventArgs e)
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

            var remoteView = new RemoteViewWindow(new ConnectionInfo { IP = ip, Port = port });
            remoteView.Closed += (s, args) => this.Close(); // Close ConnectWindow when RemoteView closes
            remoteView.Show();
            this.Hide();
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
    }
}
