using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Conduce bucla agentică (modul Edit). Ține transcriptul Anthropic (mesaje +
    /// tool_use/tool_result) pe client — serverul e stateless per rundă:
    ///
    ///   rundă:  POST /api/agent {transcript, tools} ─► "tool_calls" sau "done"
    ///   tool_calls → execuție pe thread-ul Revit (prin execTool, care face
    ///   enqueue + ExternalEvent.Raise și așteaptă rezultatul), apoi rundă nouă.
    /// </summary>
    public class AgentRunner
    {
        private const int MaxRounds = 16;

        private readonly NormalignApi _api;
        // name, argsJson -> rezultat (rulează tool-ul pe thread-ul Revit)
        private readonly Func<string, string, Task<string>> _execTool;

        public AgentRunner(NormalignApi api, Func<string, string, Task<string>> execTool)
        {
            _api = api;
            _execTool = execTool;
        }

        public async Task RunAsync(string question, string? chatId, string fileName, string context,
            JsonArray toolsDeclaration, Action<JsonObject> emit, CancellationToken ct)
        {
            var transcript = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = question }
            };
            string? activeChatId = chatId;

            for (int round = 0; round < MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                emit(new JsonObject { ["type"] = "stage", ["label"] = round == 0 ? "Analizez cererea…" : "Continui analiza…" });

                JsonObject resp = await _api.AgentRoundAsync(
                    question, activeChatId, transcript, toolsDeclaration, fileName, context, ct);

                string type = resp["type"]?.GetValue<string>() ?? "";

                if (type == "done")
                {
                    emit(new JsonObject
                    {
                        ["type"] = "reply",
                        ["content"] = resp["content"]?.GetValue<string>() ?? "",
                        ["chatId"] = resp["chatId"]?.DeepClone(),
                        ["citations"] = resp["metadata"]?["citations"]?.DeepClone(),
                        ["sourceTypes"] = resp["metadata"]?["sourceTypes"]?.DeepClone(),
                    });
                    return;
                }

                if (type != "tool_calls")
                    throw new Exception(resp["error"]?.GetValue<string>() ?? "Răspuns neașteptat de la agent.");

                if (resp["chatId"] is JsonValue cid && cid.TryGetValue(out string? cidStr) && !string.IsNullOrEmpty(cidStr))
                    activeChatId = cidStr;

                // Tura asistentului (cu tool_use) intră în transcript ca atare.
                transcript.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = resp["assistant"]?.DeepClone() ?? new JsonArray(),
                });

                var results = new JsonArray();

                // Căutările documentare — deja rezolvate de server în această rundă.
                if (resp["serverResults"] is JsonArray serverResults)
                    foreach (JsonNode? sr in serverResults)
                    {
                        if (sr is not JsonObject s) continue;
                        emit(ToolChip(s["name"]?.GetValue<string>() ?? "căutare documentară", "done"));
                        results.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = s["tool_use_id"]?.DeepClone(),
                            ["content"] = s["content"]?.DeepClone() ?? "",
                        });
                    }

                // Tool-urile Revit — executate local, pe thread-ul Revit, pe rând.
                if (resp["toolCalls"] is JsonArray toolCalls)
                    foreach (JsonNode? tc in toolCalls)
                    {
                        if (tc is not JsonObject call) continue;
                        string name = call["name"]?.GetValue<string>() ?? "?";
                        string id = call["id"]?.GetValue<string>() ?? "";
                        string args = call["input"]?.ToJsonString() ?? "{}";

                        emit(ToolChip(name, "run"));
                        string result;
                        try
                        {
                            result = await _execTool(name, args).WaitAsync(ct);
                            emit(ToolChip(name, "done"));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            result = $"Eroare: {ex.Message}";
                            emit(ToolChip(name, "error"));
                        }

                        results.Add(new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = id,
                            ["content"] = WrapResult(result),
                        });
                    }

                transcript.Add(new JsonObject { ["role"] = "user", ["content"] = results });

                // Imaginile din rundele mai vechi se înlocuiesc cu un text scurt —
                // altfel transcriptul crește necontrolat (și lovește limita de body).
                PruneOldImages(transcript);
            }

            throw new Exception("Agentul a depășit numărul maxim de runde. Încearcă o cerere mai punctuală.");
        }

        private static JsonObject ToolChip(string name, string status) => new JsonObject
        {
            ["type"] = "tool_activity",
            ["name"] = name,
            ["status"] = status,
        };

        /// <summary>
        /// Rezultatele-imagine (get_view_snapshot) devin blocuri de imagine pentru
        /// vision; restul rămân text simplu.
        /// </summary>
        private static JsonNode WrapResult(string result)
        {
            try
            {
                JsonNode? parsed = JsonNode.Parse(result);
                string? b64 = parsed?["image_base64"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    return new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "image",
                            ["source"] = new JsonObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = parsed!["media_type"]?.GetValue<string>() ?? "image/png",
                                ["data"] = b64,
                            },
                        },
                    };
                }
            }
            catch { /* nu era JSON — text simplu */ }
            return JsonValue.Create(result);
        }

        private static void PruneOldImages(JsonArray transcript)
        {
            // Păstrăm doar imaginea din ULTIMUL mesaj user (cel abia adăugat).
            for (int i = 0; i < transcript.Count - 1; i++)
            {
                if (transcript[i] is not JsonObject msg) continue;
                if (msg["role"]?.GetValue<string>() != "user") continue;
                if (msg["content"] is not JsonArray blocks) continue;
                foreach (JsonNode? b in blocks)
                {
                    if (b is not JsonObject block) continue;
                    if (block["type"]?.GetValue<string>() != "tool_result") continue;
                    if (block["content"] is JsonArray inner &&
                        inner.Count > 0 && inner[0] is JsonObject first &&
                        first["type"]?.GetValue<string>() == "image")
                    {
                        block["content"] = JsonValue.Create("(captură de view trimisă anterior — cere una nouă dacă mai e nevoie)");
                    }
                }
            }
        }
    }
}
