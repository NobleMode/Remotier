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
            _hostService.ChatReceived += OnChatReceived;

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
                _chatMessages.Clear(); // "Enable and clear it" per user request kind of?
                // User said: "disable it and clear it"
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

        private bool _isApplyingPreset = false;

        private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) RestartService();
        }

        private void FpsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingPreset) return;

            // If user changes FPS manually, set preset to Custom
            SetPresetToCustom();

            if (IsLoaded) RestartService();
        }

        private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_hostService != null)
            {
                _hostService.UpdateSettings((int)e.NewValue);
            }

            if (!_isApplyingPreset && IsLoaded)
            {
                SetPresetToCustom();
            }
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                ApplyPreset(tag);
            }
        }

        private void ApplyPreset(string tag)
        {
            _isApplyingPreset = true;
            try
            {
                if (QualitySlider == null) return;

                switch (tag)
                {
                    case "Speed":
                        SetFps(60);
                        QualitySlider.Value = 65;
                        break;
                    case "Balanced":
                        SetFps(60);
                        QualitySlider.Value = 75;
                        break;
                    case "Quality":
                        SetFps(60);
                        QualitySlider.Value = 90;
                        break;
                    case "Custom":
                        // Do nothing, keep current
                        break;
                }
            }
            finally
            {
                _isApplyingPreset = false;
            }

            // If we changed settings, service needs restart or update
            // QualitySlider updates service live. FPS change triggers RestartService via SelectionChanged if we weren't guarding? 
            // We are guarding FpsCombo_SelectionChanged with _isApplyingPreset so it won't restart.
            // We should restart if FPS changed.
            // Actually, QualitySlider update is live, but FPS needs restart.
            if (IsLoaded && tag != "Custom") RestartService();
        }

        private void SetFps(int fps)
        {
            if (FpsCombo == null) return;
            foreach (ComboBoxItem item in FpsCombo.Items)
            {
                if (item.Tag.ToString() == fps.ToString())
                {
                    FpsCombo.SelectedItem = item;
                    break;
                }
            }
        }

        private void SetPresetToCustom()
        {
            if (PresetCombo == null) return;
            foreach (ComboBoxItem item in PresetCombo.Items)
            {
                if (item.Tag.ToString() == "Custom")
                {
                    PresetCombo.SelectedItem = item;
                    break;
                }
            }
        }

        private void RestartService()
        {
            StartHosting();
        }
    }
}
