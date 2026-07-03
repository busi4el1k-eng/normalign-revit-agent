using System;
using System.IO;
using System.Text.Json;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Configurația add-in-ului. Doar URL-ul backend-ului — autentificarea se face
    /// prin login în browser (token salvat criptat de AuthStore). Fără secrete aici.
    ///
    /// Override opțional (ex. dev local) fără rebuild:
    ///   %APPDATA%\NormalignRevitAgent\config.json
    ///   { "webUrl": "http://localhost:3000" }
    /// </summary>
    public static class Config
    {
        private const string DefaultWebUrl = "https://normalign.com";

        public static string WebUrl { get; } = Load();

        /// <summary>Profilul WebView2 (persistă starea browserului între sesiuni).</summary>
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
                    if (doc.RootElement.TryGetProperty("webUrl", out JsonElement u) &&
                        u.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(u.GetString()))
                        return u.GetString()!.TrimEnd('/');
                }
            }
            catch { /* configurație coruptă -> valoarea implicită */ }
            return DefaultWebUrl;
        }
    }
}
