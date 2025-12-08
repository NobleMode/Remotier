using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Remotier.Services;

public static class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recent_connections.txt");

    public static List<string> LoadRecentConnections()
    {
        if (!File.Exists(ConfigPath)) return new List<string>();

        try
        {
            return File.ReadAllLines(ConfigPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .Take(10)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void SaveConnection(string ip)
    {
        try
        {
            var recents = LoadRecentConnections();
            recents.Remove(ip); // Remove if exists to move to top
            recents.Insert(0, ip);

            File.WriteAllLines(ConfigPath, recents.Take(10));
        }
        catch { }
    }
}
