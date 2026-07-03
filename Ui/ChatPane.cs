using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NormalignRevitAgent.Services;

namespace NormalignRevitAgent.Ui
{
    /// <summary>
    /// Panoul dockable: găzduiește UI-ul de chat propriu (Assets/chat.html —
    /// stil minimal, ca extensia Claude Code din VS Code) într-un WebView2.
    /// Nu încarcă niciun site: pagina e servită local prin virtual-host mapping,
    /// iar apelurile HTTP către backend le face C#-ul (fără CORS, cu cheia API).
    ///
    /// Punte JS <-> C#:
    ///   JS -> C#: { type: "ready" | "send" | "history" | "messages" | "open" }
    ///   C# -> JS: { type: "context" | "reply" | "history" | "messages" | "error" }
    /// </summary>
    public class ChatPane : UserControl
    {
        private const string VirtualHost = "app.normalign";

        private readonly WebView2 _web = new();
        private readonly TextBlock _fallback;

        /// <summary>Pagina s-a montat — vrea contextul curent din Revit.</summary>
        public event Action? Ready;
        /// <summary>Utilizatorul a trimis o întrebare (mesaj, chatId, mod aprofundat).</summary>
        public event Action<string, string?, bool>? SendRequested;
        /// <summary>Utilizatorul a apăsat Stop.</summary>
        public event Action? StopRequested;
        /// <summary>UI-ul cere lista de conversații.</summary>
        public event Action? HistoryRequested;
        /// <summary>UI-ul cere mesajele unei conversații.</summary>
        public event Action<string>? MessagesRequested;

        public ChatPane()
        {
            _fallback = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0x93, 0x8c)),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 320,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x1e, 0x1c)) };
            root.Children.Add(_web);
            root.Children.Add(_fallback);
            Content = root;

            _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1f, 0x1e, 0x1c);
            Loaded += async (_, _) => await EnsureInitializedAsync();
        }

        private bool _initStarted;

        private async System.Threading.Tasks.Task EnsureInitializedAsync()
        {
            if (_initStarted) return;
            _initStarted = true;

            try
            {
                var env = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Config.WebViewUserDataFolder);
                await _web.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                // Servește Assets/ local — fără rețea, fără site.
                string assets = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Assets");
                core.SetVirtualHostNameToFolderMapping(
                    VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += OnWebMessage;
                core.Navigate($"https://{VirtualHost}/chat.html");
            }
            catch (Exception ex)
            {
                _fallback.Text =
                    "Nu am putut porni componenta de browser (WebView2). " +
                    "Instalează \"WebView2 Evergreen Runtime\" de la Microsoft și repornește Revit.\n\n" +
                    ex.Message;
                _fallback.Visibility = Visibility.Visible;
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(e.TryGetWebMessageAsString()); }
            catch { return; }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                string? type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;

                switch (type)
                {
                    case "ready":
                        Ready?.Invoke();
                        break;

                    case "send":
                        string msg = root.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
                        string? chatId = root.TryGetProperty("chatId", out JsonElement c) &&
                                         c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        bool deep = root.TryGetProperty("deepThink", out JsonElement dt) &&
                                    dt.ValueKind == JsonValueKind.True;
                        if (!string.IsNullOrWhiteSpace(msg)) SendRequested?.Invoke(msg, chatId, deep);
                        break;

                    case "stop":
                        StopRequested?.Invoke();
                        break;

                    case "history":
                        HistoryRequested?.Invoke();
                        break;

                    case "messages":
                        if (root.TryGetProperty("chatId", out JsonElement id) &&
                            id.ValueKind == JsonValueKind.String)
                            MessagesRequested?.Invoke(id.GetString()!);
                        break;

                    case "open":
                        if (root.TryGetProperty("url", out JsonElement u) &&
                            u.ValueKind == JsonValueKind.String &&
                            Uri.TryCreate(u.GetString(), UriKind.Absolute, out Uri? uri) &&
                            (uri.Scheme == "https" || uri.Scheme == "http"))
                        {
                            try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
                            catch { }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Trimite un mesaj către UI. Sigur de pe orice thread — se mută singur
        /// pe thread-ul de UI.
        /// </summary>
        public void PostJson(JsonObject message)
        {
            string json = message.ToJsonString();
            Dispatcher.BeginInvoke(() => _web.CoreWebView2?.PostWebMessageAsJson(json));
        }
    }
}
