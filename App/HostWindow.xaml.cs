using System;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Remotier.Services;
using Remotier.Models;

namespace Remotier
{
    public partial class HostWindow : Window
    {
        private HostService _hostService;
        private DispatcherTimer _timer;
        private DateTime _startTime;
        private ObservableCollection<string> _clients;

        public HostWindow()
        {
            InitializeComponent();
            _clients = new ObservableCollection<string>();
            ClientList.ItemsSource = _clients;

            // Populate Monitors
            int monitorCount = CaptureService.GetMonitorCount();
            for (int i = 0; i < monitorCount; i++)
            {
                MonitorCombo.Items.Add(new ComboBoxItem { Content = $"Monitor {i + 1}", Tag = i });
            }
            if (MonitorCombo.Items.Count > 0) MonitorCombo.SelectedIndex = 0;

            // Start Timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _startTime = DateTime.Now;
            _timer.Start();

            StartHosting();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            var diff = DateTime.Now - _startTime;
            TimerText.Text = diff.ToString(@"hh\:mm\:ss");
        }

        private async void StartHosting()
        {
            StatusText.Text = "Starting services...";

            _hostService?.Stop(); // Ensure stopped before starting
            _hostService = new HostService();

            // Wire events
            _hostService.ClientConnected += OnClientConnected;
            _hostService.ClientDisconnected += OnClientDisconnected;

            try
            {
                int monitorIndex = MonitorCombo.SelectedIndex >= 0 ? (int)((ComboBoxItem)MonitorCombo.SelectedItem).Tag : 0;

                int fps = 60;
                if (FpsCombo.SelectedItem is ComboBoxItem fpsItem && int.TryParse(fpsItem.Tag.ToString(), out int parsedFps))
                {
                    fps = parsedFps;
                }

                int quality = (int)QualitySlider.Value;

                var options = new StreamOptions
                {
                    Quality = quality,
                    EnableScaling = false
                };

                // Get local IP
                string localIp = await GetLocalIpAddress();
                IpText.Text = localIp;

                _hostService.Start(5000, options, monitorIndex, fps);
                StatusText.Text = $"Hosting on {localIp}:5000";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting host: {ex.Message}", "Error");
                StatusText.Text = "Error starting host.";
            }
        }

        private void OnClientConnected(string endpoint)
        {
            Dispatcher.Invoke(() =>
            {
                _clients.Add(endpoint);
                StatusText.Text = $"Client connected: {endpoint}";
            });
        }

        private void OnClientDisconnected(string endpoint)
        {
            Dispatcher.Invoke(() =>
            {
                _clients.Remove(endpoint);
                if (_clients.Count == 0) StatusText.Text = "Waiting for connections...";
            });
        }

        private async System.Threading.Tasks.Task<string> GetLocalIpAddress()
        {
            return await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                    {
                        socket.Connect("8.8.8.8", 65530);
                        IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                        return endPoint.Address.ToString();
                    }
                }
                catch
                {
                    return "127.0.0.1";
                }
            });
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyIp_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(IpText.Text);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _hostService?.Stop();
            _timer?.Stop();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Already handled
        }

        private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) RestartService();
        }

        private void FpsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) RestartService();
        }

        private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_hostService != null)
            {
                _hostService.UpdateSettings((int)e.NewValue);
            }
        }

        private void RestartService()
        {
            StartHosting();
        }
    }
}
