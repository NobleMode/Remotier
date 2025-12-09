using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Remotier
{
    public partial class ChatWindow : Window
    {
        public event Action<string> MessageSent = delegate { };
        public ObservableCollection<string> Messages { get; } = new ObservableCollection<string>();

        public ChatWindow()
        {
            InitializeComponent();
            MessageList.ItemsSource = Messages;
        }

        public void AddMessage(string sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                Messages.Add($"{sender}: {text}");
                if (Messages.Count > 0)
                    MessageList.ScrollIntoView(Messages[Messages.Count - 1]);
            });
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
            }
        }

        private void SendMessage()
        {
            string text = InputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            MessageSent?.Invoke(text);
            AddMessage("Me", text);
            InputBox.Text = "";
        }
    }
}
