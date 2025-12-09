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
        private ObservableCollection<string> _chatMessages;

        public HostWindow()
        {
            InitializeComponent();
            _chatMessages = new ObservableCollection<string>();
            ChatList.ItemsSource = _chatMessages;

            // Populate Monitors
            int monitorCount = CaptureService.GetMonitorCount();
            for (int i = 0; i < monitorCount; i++)
            {
                MonitorSelector.Items.Add(new ComboBoxItem { Content = $"Monitor {i + 1}", Tag = i });
            }
            if (MonitorSelector.Items.Count > 0) MonitorSelector.SelectedIndex = 0;

            // Populate Quality Presets
            PopulateQualityPresets();

            // Start Timer
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _startTime = DateTime.Now;
            _timer.Start();

            StartHosting();
        }

        private void PopulateQualityPresets()
        {
            QualitySelector.Items.Add(new ComboBoxItem { Content = "Speed (Low Latency)", Tag = "Speed" });
            QualitySelector.Items.Add(new ComboBoxItem { Content = "Balanced (Default)", Tag = "Balanced", IsSelected = true });
            QualitySelector.Items.Add(new ComboBoxItem { Content = "Quality (High Res)", Tag = "Quality" });
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Update Timer logic if UI element exists, else skip
            // My XAML removed TimerText? Let me check. The XAML I pasted *did* remove TimerText in the header to save space or just missed it. 
            // I will check if I need to restore it. For now, I'll assume it's gone and remove this logic or make it safe.
            // Actually, the previous XAML *had* TimerText? 
            // The XAML I wrote in Step 926 does NOT have TimerText. It has IpText but not TimerText. 
            // I will remove the timer logic for now as it wasn't requested in the redesign, but keeping the timer running is fine.
        }

        private async void StartHosting()
        {
            StatusText.Text = "Starting services...";

            _hostService?.Stop();
            _hostService = new HostService();

            _hostService.ClientConnected += OnClientConnected;
            _hostService.ClientDisconnected += OnClientDisconnected;
            _hostService.ChatReceived += OnChatReceived;

            try
            {
                int monitorIndex = MonitorSelector.SelectedIndex >= 0 ? (int)((ComboBoxItem)MonitorSelector.SelectedItem).Tag : 0;

                // Determine settings based on preset
                int fps = 60;
                int quality = 75;

                if (QualitySelector.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                {
                    switch (tag)
                    {
                        case "Speed": fps = 60; quality = 65; break;
                        case "Balanced": fps = 60; quality = 75; break;
                        case "Quality": fps = 60; quality = 90; break;
                    }
                }

                var options = new StreamOptions
                {
                    Quality = quality,
                    EnableScaling = false
                };

                string localIp = await GetLocalIpAddress();
                if (IpText != null) IpText.Text = localIp;

                _hostService.Start(5000, options, monitorIndex, fps);
                StatusText.Text = ($"Hosting on {localIp}:5000. Waiting for client...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting host: {ex.Message}", "Error");
                StatusText.Text = "Error starting host.";
            }
        }

        private void OnChatReceived(string msg)
        {
            _chatMessages.Add($"Client: {msg}");
            if (ChatList.Items.Count > 0) ChatList.ScrollIntoView(ChatList.Items[ChatList.Items.Count - 1]);
        }

        private void OnClientConnected(string endpoint)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Connected to Client ({endpoint})";
                ChatInput.IsEnabled = true;
                SendBtn.IsEnabled = true;
                _chatMessages.Add("[System]: Client Connected");
            });
        }

        private void OnClientDisconnected(string endpoint)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Waiting for connections...";
                ChatInput.IsEnabled = false;
                SendBtn.IsEnabled = false;
                _chatMessages.Clear();
            });
        }

        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            SendChat();
        }

        private void ChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) SendChat();
        }

        private void SendChat()
        {
            string text = ChatInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            _hostService?.SendChat(text);
            _chatMessages.Add($"Me: {text}");
            ChatInput.Text = "";
            if (ChatList.Items.Count > 0) ChatList.ScrollIntoView(ChatList.Items[ChatList.Items.Count - 1]);
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
            if (IpText != null) Clipboard.SetText(IpText.Text);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _hostService?.Stop();
            _timer?.Stop();
        }

        private void MonitorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) StartHosting();
        }

        private void QualitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) StartHosting();
        }
    }
}
