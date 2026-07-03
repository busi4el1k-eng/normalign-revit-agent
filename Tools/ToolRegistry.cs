using System.Collections.Generic;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// Holds every Revit capability the agent can use, keyed by tool name.
    ///
    /// v1 only exposes get_model_summary. To go agentic, add more IRevitTool
    /// implementations here and have the server-side agent loop select one by
    /// name — no other part of the add-in has to change.
    /// </summary>
    public class ToolRegistry
    {
        public readonly GetModelSummaryTool GetModelSummary = new();

        private readonly Dictionary<string, IRevitTool> _byName;

        public ToolRegistry()
        {
            var all = new IRevitTool[]
            {
                GetModelSummary,
                // v2: new QueryElementsTool(), new TagElementTool(), ...
            };
            _byName = new Dictionary<string, IRevitTool>();
            foreach (var t in all)
                _byName[t.Name] = t;
        }

        public bool TryGet(string name, out IRevitTool tool) => _byName.TryGetValue(name, out tool!);

        public IEnumerable<IRevitTool> All => _byName.Values;
    }
}
