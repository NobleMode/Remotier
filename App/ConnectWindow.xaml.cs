using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Remotier.Models;
using Remotier.Services;

namespace Remotier
{
    public partial class ConnectWindow : Window
    {
        private ObservableCollection<string> _recentConnections;

        public ConnectWindow()
        {
            InitializeComponent();
            LoadRecents();
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
