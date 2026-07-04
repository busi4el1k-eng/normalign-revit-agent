using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// Toate capabilitățile Revit ale agentului, pe nume. Bucla de agent de pe
    /// server primește declarația (nume + descriere + schema argumentelor) prin
    /// <see cref="Declare"/>; execuția se face pe thread-ul Revit prin
    /// RevitRequestHandler (TryGet + Execute).
    /// </summary>
    public class ToolRegistry
    {
        public readonly GetModelSummaryTool GetModelSummary = new();

        private readonly Dictionary<string, IRevitTool> _byName;

        public ToolRegistry()
        {
            var all = new IRevitTool[]
            {
                // citire
                GetModelSummary,
                new QueryElementsTool(),
                new GetElementDetailsTool(),
                new GetSelectionTool(),
                new ListLevelsAndGridsTool(),
                new ListFamilyTypesTool(),
                new GetActiveViewTool(),
                new GetModelWarningsTool(),
                new GetViewSnapshotTool(),
                new SelectAndShowTool(),
                // scriere (doar modul Edit)
                new SetParametersTool(),
                new ChangeElementTypeTool(),
                new MoveElementsTool(),
                new DeleteElementsTool(),
                new OverrideColorInViewTool(),
                new IsolateInViewTool(),
            };
            _byName = new Dictionary<string, IRevitTool>();
            foreach (var t in all)
                _byName[t.Name] = t;
        }

        public bool TryGet(string name, out IRevitTool tool) => _byName.TryGetValue(name, out tool!);

        public IEnumerable<IRevitTool> All => _byName.Values;

        /// <summary>
        /// Declarația tool-urilor pentru /api/agent (format Anthropic tools).
        /// Cu includeWrite=false rămân doar tool-urile de citire.
        /// </summary>
        public JsonArray Declare(bool includeWrite)
        {
            var arr = new JsonArray();
            foreach (IRevitTool t in _byName.Values)
            {
                if (t.IsWrite && !includeWrite) continue;
                arr.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = JsonNode.Parse(t.InputSchema),
                });
            }
            return arr;
        }
    }
}
