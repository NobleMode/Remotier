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

            // Subscribe to events
            _hostService.ClientConnected += OnClientConnected;
            _hostService.ClientDisconnected += OnClientDisconnected;
            _hostService.ChatReceived += OnChatReceived;
            _hostService.OnCaptureTiming += (ms) => Dispatcher.Invoke(() =>
            {
                HostStats.Text = $"Enc: {ms:F1}ms";
            });
            _hostService.OnFpsUpdate += (fps) => Dispatcher.Invoke(() => HostStats.Text = $"{fps} FPS");
            _hostService.OnAuthenticationFailed += (reason) =>
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = "Authentication Failed!";
                    _chatMessages.Add($"[System] {reason}");
                });
            };

            // Handle File Transfer Security
            _hostService.RequestFileAcceptance += (name, size) =>
            {
                return Dispatcher.Invoke(() =>
                {
                    string sizeStr = size > 1024 * 1024 ? $"{size / 1024 / 1024} MB" : $"{size / 1024} KB";
                    var result = MessageBox.Show($"Incoming File Request:\n\nName: {name}\nSize: {sizeStr}\n\nDo you want to accept and download this file?", "File Transfer Request", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    return result == MessageBoxResult.Yes;
                });
            };

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

                SessionIdText.Text = _hostService.SessionId;
                SessionPassText.Text = _hostService.SessionPassword;

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
                if (AcceptAllCheck.IsChecked == true)
                {
                    StatusText.Text = $"Connected to Client ({endpoint})";
                    ChatInput.IsEnabled = true;
                    SendBtn.IsEnabled = true;
                    _chatMessages.Add("[System]: Client Connected (Auto-Accepted)");
                    SessionInfoGrid.Visibility = Visibility.Collapsed;
                    return;
                }

                // Security: Prompt user to accept connection
                var result = MessageBox.Show($"Allow connection from {endpoint}?\n\nThis client will be able to view your screen and control input.", "Connection Request", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    StatusText.Text = $"Connected to Client ({endpoint})";
                    ChatInput.IsEnabled = true;
                    SendBtn.IsEnabled = true;
                    _chatMessages.Add("[System]: Client Connected");
                    SessionInfoGrid.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _hostService.DisconnectClient();
                    StatusText.Text = "Connection Denied. Waiting...";
                    _chatMessages.Add($"[System]: Connection from {endpoint} denied.");
                }
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
                SessionInfoGrid.Visibility = Visibility.Visible;
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

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (_hostService != null)
            {
                var settings = new SettingWindow(_hostService);
                settings.Owner = this;
                settings.ShowDialog();
            }
        }

        private void CopyIp_Click(object sender, RoutedEventArgs e)
        {
            if (IpText != null) Clipboard.SetText(IpText.Text);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                TrayIcon.Visibility = Visibility.Visible;
                TrayIcon.ShowBalloonTip("Remotier", "Minimised to Tray", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }

        private void TrayShow_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            TrayIcon.Visibility = Visibility.Collapsed;
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            _hostService?.Stop();
            TrayIcon.Dispose();
            Application.Current.Shutdown();
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            TrayShow_Click(sender, e);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If user clicked X, just close app for simplicity in this version, or minimalize?
            // "Stop Hosting" button calls Close().
            // Let's ensure proper cleanup.
            _hostService?.Stop();
            _timer?.Stop();
            TrayIcon.Dispose();
        }

        private void MonitorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) StartHosting();
        }

        private void QualitySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) StartHosting();
        }

        private void SessionIdText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(SessionIdText.Text) && _hostService != null)
            {
                Clipboard.SetText(SessionIdText.Text);
                StatusText.Text = "Session ID Copied!";
                // Reset status after short delay? 
                // Simple feedback is enough for now.
            }
        }

        private void SessionPassText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!string.IsNullOrEmpty(SessionPassText.Text) && _hostService != null)
            {
                Clipboard.SetText(SessionPassText.Text);
                StatusText.Text = "Session Password Copied!";
            }
        }
    }
}
