using System.Windows;

namespace Remotier;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Host_Click(object sender, RoutedEventArgs e)
    {
        var check = Remotier.Services.HostService.CheckRequirements();
        if (!check.Success)
        {
            MessageBox.Show(check.Error, "Requirement Check Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        else
        {
            // Show success details as requested
            MessageBox.Show(check.Error, "Requirement Check Passed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        var hostWindow = new HostWindow();
        hostWindow.Closed += (s, args) => this.Show();
        hostWindow.Show();
        this.Hide();
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        var connectWindow = new ConnectWindow();
        connectWindow.Closed += (s, args) => this.Show();
        connectWindow.Show();
        this.Hide();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }
}