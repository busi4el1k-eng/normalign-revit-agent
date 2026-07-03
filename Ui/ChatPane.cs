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
using NormalignRevitAgent.Revit;
using NormalignRevitAgent.Services;

namespace NormalignRevitAgent.Ui
{
    /// <summary>
    /// Panoul dockable: găzduiește UI-ul local (Assets/*.html — stil Claude Code)
    /// într-un WebView2. La pornire arată login.html sau chat.html după cum există
    /// un token salvat. HTTP-ul îl face C#-ul, cu token-ul de desktop (Bearer).
    /// </summary>
    public class ChatPane : UserControl
    {
        private const string VirtualHost = "app.normalign";

        private readonly WebView2 _web = new();
        private readonly TextBlock _fallback;
        private bool _coreReady;

        public event Action? Ready;
        public event Action<string, string?, bool>? SendRequested;
        public event Action? StopRequested;
        public event Action? HistoryRequested;
        public event Action<string>? MessagesRequested;
        public event Action? LoginRequested;
        public event Action? LogoutRequested;

        public ChatPane()
        {
            _fallback = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x97, 0x93, 0x8c)),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                MaxWidth = 320, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed
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
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Config.WebViewUserDataFolder);
                await _web.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                string assets = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Assets");
                core.SetVirtualHostNameToFolderMapping(
                    VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);

                core.WebMessageReceived += OnWebMessage;
                _coreReady = true;
                NavigateFor(AuthStore.IsLoggedIn);
            }
            catch (Exception ex)
            {
                _fallback.Text =
                    "Nu am putut porni componenta de browser (WebView2). " +
                    "Instalează \"WebView2 Evergreen Runtime\" de la Microsoft și repornește Revit.\n\n" + ex.Message;
                _fallback.Visibility = Visibility.Visible;
            }
        }

        private void NavigateFor(bool loggedIn) =>
            _web.CoreWebView2?.Navigate($"https://{VirtualHost}/{(loggedIn ? "chat" : "login")}.html");

        public void ShowChat() => Dispatcher.BeginInvoke(() => NavigateFor(true));
        public void ShowLogin() => Dispatcher.BeginInvoke(() => NavigateFor(false));
        public void LoginFailed(string message) =>
            PostJson(new JsonObject { ["type"] = "login-failed", ["message"] = message });

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
                    case "login-ready":
                        PostJson(new JsonObject { ["type"] = "theme", ["theme"] = RevitRequestHandler.RevitThemeName() });
                        break;
                    case "login":
                        LoginRequested?.Invoke();
                        break;
                    case "logout":
                        LogoutRequested?.Invoke();
                        break;
                    case "send":
                        string msg = root.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
                        string? chatId = root.TryGetProperty("chatId", out JsonElement c) &&
                                         c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        bool deep = root.TryGetProperty("deepThink", out JsonElement dt) && dt.ValueKind == JsonValueKind.True;
                        if (!string.IsNullOrWhiteSpace(msg)) SendRequested?.Invoke(msg, chatId, deep);
                        break;
                    case "stop":
                        StopRequested?.Invoke();
                        break;
                    case "history":
                        HistoryRequested?.Invoke();
                        break;
                    case "messages":
                        if (root.TryGetProperty("chatId", out JsonElement id) && id.ValueKind == JsonValueKind.String)
                            MessagesRequested?.Invoke(id.GetString()!);
                        break;
                    case "open":
                        if (root.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String &&
                            Uri.TryCreate(u.GetString(), UriKind.Absolute, out Uri? uri) &&
                            (uri.Scheme == "https" || uri.Scheme == "http"))
                        {
                            try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); } catch { }
                        }
                        break;
                }
            }
        }

        /// <summary>Trimite un mesaj către UI. Sigur de pe orice thread.</summary>
        public void PostJson(JsonObject message)
        {
            string json = message.ToJsonString();
            Dispatcher.BeginInvoke(() => { if (_coreReady) _web.CoreWebView2?.PostWebMessageAsJson(json); });
        }
    }
}
