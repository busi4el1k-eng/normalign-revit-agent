using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Ui
{
    /// <summary>
    /// Tells Revit which WPF element to host inside the dockable pane,
    /// and where to dock it by default.
    /// </summary>
    public class ChatPaneProvider : IDockablePaneProvider
    {
        private readonly ChatPane _pane;

        public ChatPaneProvider(ChatPane pane) => _pane = pane;

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = _pane;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }
    }
}
