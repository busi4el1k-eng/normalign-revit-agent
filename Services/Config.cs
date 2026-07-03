using System;
using System.IO;
using System.Text.Json;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Configuration for the add-in. The pane hosts the real Normalign web app,
    /// so the only setting that matters is which URL to load.
    ///
    /// Override per-machine (e.g. local dev) without rebuilding by creating:
    ///   %APPDATA%\NormalignRevitAgent\config.json
    ///   { "webUrl": "http://localhost:3000" }
    /// </summary>
    public static class Config
    {
        private const string DefaultWebUrl = "https://normalign.com";

        public static string WebUrl { get; } = Load();

        /// <summary>Browser profile folder — keeps the Clerk login persistent between sessions.</summary>
        public static string WebViewUserDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "NormalignRevitAgent", "WebView2");

        private static string Load()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                           "NormalignRevitAgent", "config.json");
                if (File.Exists(path))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("webUrl", out JsonElement url) &&
                        url.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(url.GetString()))
                        return url.GetString()!.TrimEnd('/');
                }
            }
            catch { /* fall back to default on any parse/IO problem */ }
            return DefaultWebUrl;
        }
    }
}
