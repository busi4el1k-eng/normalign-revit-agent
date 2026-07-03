using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NormalignRevitAgent.Tools;

namespace NormalignRevitAgent.Revit
{
    /// <summary>
    /// The threading seam between the embedded web app and the Revit API.
    ///
    /// The Revit API is only legal on Revit's thread, inside a Revit-raised
    /// event. The WebView2 "ready" message arrives on the UI thread, so we mark
    /// a pending sync and Raise() the ExternalEvent; Revit then calls Execute()
    /// in a valid context where the model can be read.
    ///
    /// v2 (agentic): the same Execute() will dispatch LLM tool calls from the
    /// ToolRegistry (query_elements, tag_element, ...) — only the message
    /// payload grows, the threading model stays identical.
    /// </summary>
    public class RevitRequestHandler : IExternalEventHandler
    {
        private readonly ToolRegistry _tools = new();
        private int _syncPending;
        private string? _lastSyncedTitle;

        /// <summary>Called from any thread. Pair with App.RevitEvent.Raise().</summary>
        public void RequestSync() => Interlocked.Exchange(ref _syncPending, 1);

        public string GetName() => "Normalign Agent";

        // Runs on Revit's thread (ExternalEvent).
        public void Execute(UIApplication app)
        {
            if (Interlocked.Exchange(ref _syncPending, 0) == 0) return;
            SyncFromDoc(app.ActiveUIDocument?.Document, force: true);
        }

        /// <summary>
        /// Extract the live model summary and push it into the web app.
        /// Must be called in a valid Revit API context (Execute or a Revit event
        /// like ViewActivated). Skips the work if this document was already sent,
        /// unless <paramref name="force"/> is set.
        /// </summary>
        public void SyncFromDoc(Document? doc, bool force = false)
        {
            string title = doc?.Title ?? "";
            if (!force && title == _lastSyncedTitle) return;
            _lastSyncedTitle = title;

            string fileName = string.IsNullOrEmpty(title) ? "Model Revit" : $"{title}.rvt";
            string summary = _tools.GetModelSummary.Execute(doc, "{}");
            App.Pane?.PostModel(fileName, summary);
        }
    }
}
