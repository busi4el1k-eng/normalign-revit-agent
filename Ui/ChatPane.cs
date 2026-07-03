using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;
using NormalignRevitAgent.Revit;
using NormalignRevitAgent.Services;
// Autodesk.Revit.UI also defines a TextBox; disambiguate to the WPF one.
using TextBox = System.Windows.Controls.TextBox;

namespace NormalignRevitAgent.Ui
{
    /// <summary>
    /// The chat panel hosted in the dockable pane. Built in code (no XAML) to
    /// keep the project self-contained. Layout: scrolling message list on top,
    /// input box + Send button at the bottom.
    ///
    /// Flow when the user sends a message:
    ///   1. Add the user's bubble to the list.
    ///   2. Enqueue a ChatRequest and Raise the ExternalEvent — this hops onto
    ///      Revit's thread (see RevitRequestHandler) so the model can be read.
    ///   3. The reply callback marshals back to the UI thread via Dispatcher.
    /// </summary>
    public class ChatPane : UserControl
    {
        private readonly ExternalEvent _revitEvent;
        private readonly RevitRequestHandler _handler;

        private readonly StackPanel _messages;
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input;
        private readonly Button _send;

        private string? _chatId; // keeps the conversation threaded server-side

        public ChatPane(ExternalEvent revitEvent, RevitRequestHandler handler)
        {
            _revitEvent = revitEvent;
            _handler = handler;

            _messages = new StackPanel { Margin = new Thickness(8) };
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _messages
            };
            Grid.SetRow(_scroll, 0);

            _input = new TextBox
            {
                AcceptsReturn = false,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 46,
                Margin = new Thickness(8, 4, 4, 8),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; OnSend(); }
            };

            _send = new Button
            {
                Content = "Trimite",
                MinWidth = 80,
                Margin = new Thickness(0, 4, 8, 8)
            };
            _send.Click += (_, _) => OnSend();

            var inputRow = new DockPanel();
            DockPanel.SetDock(_send, Dock.Right);
            inputRow.Children.Add(_send);
            inputRow.Children.Add(_input);
            Grid.SetRow(inputRow, 1);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(_scroll);
            grid.Children.Add(inputRow);

            Content = grid;

            AddBubble("Normalign", "Salut! Întreabă-mă despre normative sau despre modelul Revit deschis.", isUser: false);
        }

        private void OnSend()
        {
            string text = _input.Text?.Trim() ?? "";
            if (text.Length == 0) return;

            AddBubble("Tu", text, isUser: true);
            _input.Clear();
            SetBusy(true);

            var thinking = AddBubble("Normalign", "…", isUser: false);

            _handler.Enqueue(new ChatRequest
            {
                Question = text,
                ChatId = _chatId,
                OnReply = result => Dispatcher.Invoke(() =>
                {
                    _chatId = result.ChatId ?? _chatId;
                    thinking.Text = result.Content;
                    if (result.FollowUpQuestions.Count > 0)
                        thinking.Text += "\n\nÎntrebări sugerate:\n• " + string.Join("\n• ", result.FollowUpQuestions);
                    SetBusy(false);
                    ScrollToEnd();
                }),
                OnError = err => Dispatcher.Invoke(() =>
                {
                    thinking.Text = "Eroare: " + err;
                    thinking.Foreground = Brushes.IndianRed;
                    SetBusy(false);
                    ScrollToEnd();
                })
            });

            // Hop onto Revit's thread to read the model + call the backend.
            _revitEvent.Raise();
        }

        private TextBlock AddBubble(string author, string text, bool isUser)
        {
            var body = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var border = new Border
            {
                Background = isUser ? Brushes.LightSteelBlue : new SolidColorBrush(Color.FromRgb(238, 238, 238)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 8),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = 460,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = author, FontWeight = FontWeights.Bold, FontSize = 11, Opacity = 0.7 },
                        body
                    }
                }
            };

            _messages.Children.Add(border);
            ScrollToEnd();
            return body; // returned so the caller can update it in place (e.g. "…" -> answer)
        }

        private void SetBusy(bool busy)
        {
            _send.IsEnabled = !busy;
            _input.IsEnabled = !busy;
        }

        private void ScrollToEnd() => _scroll.ScrollToEnd();
    }
}
