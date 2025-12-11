using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Remotier.Models
{
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public DateTime LastSeen { get; set; }
    }

    public class ConnectionHistory
    {
        public string DeviceName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public DateTime ConnectedAt { get; set; }
    }

    public class SecuritySettings
    {
        public string AccountName { get; set; } = "";
        public string AccountPasswordHash { get; set; } = ""; // SHA256

        public List<DeviceInfo> TrustedDevices { get; set; } = new List<DeviceInfo>();
        public List<ConnectionHistory> RecentConnections { get; set; } = new List<ConnectionHistory>();

        private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Remotier", "security_settings.json");

        public static SecuritySettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<SecuritySettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new SecuritySettings();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
