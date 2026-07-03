using System;
using System.IO;
using System.Text.Json;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Configurația add-in-ului. Se citește din:
    ///   %APPDATA%\NormalignRevitAgent\config.json
    ///   { "webUrl": "https://normalign.com", "apiKey": "nrml_desk_..." }
    ///
    /// apiKey = cheia de desktop verificată de backend (src/lib/desktop-auth.ts,
    /// env DESKTOP_API_KEY) — trimisă ca "Authorization: Bearer ...".
    /// </summary>
    public static class Config
    {
        private const string DefaultWebUrl = "https://normalign.com";

        public static string WebUrl { get; }
        public static string ApiKey { get; }

        static Config()
        {
            string webUrl = DefaultWebUrl, apiKey = "";
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                           "NormalignRevitAgent", "config.json");
                if (File.Exists(path))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("webUrl", out JsonElement u) &&
                        u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString()))
                        webUrl = u.GetString()!.TrimEnd('/');
                    if (doc.RootElement.TryGetProperty("apiKey", out JsonElement k) &&
                        k.ValueKind == JsonValueKind.String)
                        apiKey = k.GetString()!.Trim();
                }
            }
            catch { /* configurație coruptă -> valori implicite */ }
            WebUrl = webUrl;
            ApiKey = apiKey;
        }

        /// <summary>Profilul WebView2 (persistă starea browserului între sesiuni).</summary>
        public static string WebViewUserDataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "NormalignRevitAgent", "WebView2");
    }
}
