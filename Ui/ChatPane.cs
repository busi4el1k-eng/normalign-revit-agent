using System;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using NormalignRevitAgent.Services;

namespace NormalignRevitAgent.Ui
{
    /// <summary>
    /// The dockable pane: hosts the real Normalign web app in WebView2, so the
    /// chat inside Revit IS the production chat — same markdown rendering,
    /// citations, style profiles, history sidebar and Clerk auth as the site.
    ///
    /// The only Revit-specific piece is the JS bridge:
    ///   web  -> host : { type: "normalign-ready" }   (page mounted, wants the model)
    ///   host -> web  : { type: "revit-model", fileName, summary }
    /// The web app then attaches the summary to every question as ifcContext —
    /// the contract the backend already understands.
    /// </summary>
    public class ChatPane : UserControl
    {
        private readonly WebView2 _web = new();
        private readonly Grid _overlay;      // dark loading / error chrome (Claude-extension style)
        private readonly TextBlock _overlayTitle;
        private readonly TextBlock _overlayDetail;

        /// <summary>Raised when the web app announced it is mounted and wants the model.</summary>
        public event Action? WebAppReady;

        private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));
        private static readonly Brush FgDim = new SolidColorBrush(Color.FromRgb(0x9d, 0x9d, 0x9d));
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xd9, 0x77, 0x57));

        public ChatPane()
        {
            _overlayTitle = new TextBlock
            {
                Text = "Normalign",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Accent,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _overlayDetail = new TextBlock
            {
                Text = "Se conectează…",
                FontSize = 12,
                Foreground = FgDim,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                MaxWidth = 320,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var overlayStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            overlayStack.Children.Add(_overlayTitle);
            overlayStack.Children.Add(_overlayDetail);

            _overlay = new Grid { Background = Bg };
            _overlay.Children.Add(overlayStack);

            var root = new Grid { Background = Bg };
            root.Children.Add(_web);
            root.Children.Add(_overlay);
            Content = root;

            _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1e, 0x1e, 0x1e);
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

                // Citation PDFs / external links open in the system browser.
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
                };

                core.WebMessageReceived += OnWebMessage;
                core.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess) HideOverlay();
                    else ShowError($"Nu am putut încărca {Config.WebUrl} (cod {e.WebErrorStatus}). Verifică conexiunea la internet.");
                };

                core.Navigate(Config.WebUrl);
            }
            catch (Exception ex)
            {
                // Most likely: WebView2 Evergreen Runtime missing on this PC.
                ShowError(
                    "Nu am putut porni componenta de browser (WebView2). " +
                    "Instalează \"WebView2 Evergreen Runtime\" de la Microsoft și repornește Revit.\n\n" +
                    ex.Message);
            }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                if (doc.RootElement.TryGetProperty("type", out JsonElement t) &&
                    t.GetString() == "normalign-ready")
                {
                    WebAppReady?.Invoke();
                }
            }
            catch { /* ignore malformed messages */ }
        }

        /// <summary>
        /// Push the live model context into the web app. Safe to call from any
        /// thread — marshals itself onto the UI thread.
        /// </summary>
        public void PostModel(string fileName, string summary)
        {
            Dispatcher.BeginInvoke(() =>
            {
                CoreWebView2? core = _web.CoreWebView2;
                if (core == null) return;
                string json = JsonSerializer.Serialize(new
                {
                    type = "revit-model",
                    fileName,
                    summary
                });
                core.PostWebMessageAsJson(json);
            });
        }

        private void HideOverlay() => _overlay.Visibility = Visibility.Collapsed;

        private void ShowError(string message)
        {
            _overlay.Visibility = Visibility.Visible;
            _overlayDetail.Text = message;
        }
    }
}
