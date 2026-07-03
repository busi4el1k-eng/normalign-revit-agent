using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Client HTTP pentru backend-ul Normalign. Autentificare prin cheia de
    /// desktop (Bearer) — vezi src/lib/desktop-auth.ts în aplicația web.
    /// </summary>
    public class NormalignApi
    {
        // Timeout infinit: fluxul deep-think poate dura minute. Token-ul se pune
        // per-cerere (se schimbă la login/logout), deci nu ca header implicit.
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        private static void Authorize(HttpRequestMessage req)
        {
            string? token = AuthStore.Token;
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        /// <summary>
        /// Trimite o întrebare cu contextul live din Revit. Mod standard = un
        /// singur răspuns JSON; mod aprofundat = flux SSE (stage/thinking/done).
        /// <paramref name="emit"/> primește mesajele deja formatate pentru UI.
        /// </summary>
        public async Task SendChatAsync(string message, string? chatId, string fileName, string context,
            bool deepThink, Action<JsonObject> emit, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["message"] = message,
                ["chatId"] = chatId,
                ["deepThink"] = deepThink,
                // Serverul adaptează prompturile (interfață Revit, model live, intent)
                ["client"] = "revit",
                ["ifcContext"] = new JsonObject { ["fileName"] = fileName, ["summary"] = context }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Config.WebUrl}/api/chat")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            Authorize(req);

            using HttpResponseMessage resp = await Http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!resp.IsSuccessStatusCode)
            {
                string err = await SafeRead(resp);
                string hint = (int)resp.StatusCode == 401
                    ? " Verifică apiKey din config.json și DESKTOP_API_KEY pe server." : "";
                throw new Exception($"Server {(int)resp.StatusCode}.{hint} {err}".Trim());
            }

            if (deepThink)
                await ReadSseAsync(resp, emit, ct);
            else
            {
                JsonNode data = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct)) ?? new JsonObject();
                emit(BuildReply(data));
            }
        }

        // ── SSE: data: {"type":"stage"|"thinking"|"done"|"error", ...}\n\n ──────
        private static async Task ReadSseAsync(HttpResponseMessage resp, Action<JsonObject> emit, CancellationToken ct)
        {
            using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                JsonNode? ev;
                try { ev = JsonNode.Parse(line.Substring(6)); } catch { continue; }
                if (ev == null) continue;

                switch (ev["type"]?.GetValue<string>())
                {
                    case "stage":
                        emit(new JsonObject { ["type"] = "stage", ["label"] = ev["label"]?.GetValue<string>() ?? "" });
                        break;
                    case "thinking":
                        emit(new JsonObject { ["type"] = "thinking", ["delta"] = ev["delta"]?.GetValue<string>() ?? "" });
                        break;
                    case "done":
                        if (ev["payload"] is JsonObject payload)
                            emit(BuildReply(payload));
                        return;
                    case "error":
                        throw new Exception(ev["error"]?.GetValue<string>() ?? "Eroare internă la analiză.");
                }
            }
        }

        private static JsonObject BuildReply(JsonNode data) => new JsonObject
        {
            ["type"] = "reply",
            ["content"] = data["content"]?.GetValue<string>() ?? "",
            ["chatId"] = data["chatId"]?.DeepClone(),
            ["followUps"] = data["followUpQuestions"]?.DeepClone(),
            ["citations"] = data["metadata"]?["citations"]?.DeepClone(),
            // doc_code -> "normative" | "fise_tehnice" — UI-ul alege reader-ul
            // potrivit (normativ = MD + PDF, fișă tehnică = doar MD).
            ["sourceTypes"] = data["metadata"]?["sourceTypes"]?.DeepClone(),
            ["thinking"] = data["metadata"]?["thinking"]?.DeepClone(),
        };

        /// <summary>
        /// O rundă a agentului (modul Edit): trimite transcriptul complet + tool-urile
        /// declarate; primește fie tool_calls de executat în Revit, fie răspunsul final.
        /// </summary>
        public async Task<JsonObject> AgentRoundAsync(string question, string? chatId,
            JsonArray transcript, JsonArray tools, string fileName, string context, CancellationToken ct)
        {
            var body = new JsonObject
            {
                ["question"] = question,
                ["chatId"] = chatId,
                ["transcript"] = transcript.DeepClone(),
                ["tools"] = tools.DeepClone(),
                ["ifcContext"] = new JsonObject { ["fileName"] = fileName, ["summary"] = context },
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{Config.WebUrl}/api/agent")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            Authorize(req);

            using HttpResponseMessage resp = await Http.SendAsync(req, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                string hint = (int)resp.StatusCode == 401
                    ? " Sesiunea a expirat — Logout și Login din nou." : "";
                string detail = "";
                try { detail = JsonNode.Parse(json)?["error"]?.GetValue<string>() ?? ""; } catch { }
                throw new Exception($"Server {(int)resp.StatusCode}.{hint} {detail}".Trim());
            }
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        public async Task<JsonNode> GetHistoryAsync()
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Config.WebUrl}/api/history");
            Authorize(req);
            using HttpResponseMessage resp = await Http.SendAsync(req);
            return await ParseAsync(resp);
        }

        public async Task<JsonNode> GetMessagesAsync(string chatId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{Config.WebUrl}/api/messages?chatId={Uri.EscapeDataString(chatId)}");
            Authorize(req);
            using HttpResponseMessage resp = await Http.SendAsync(req);
            return await ParseAsync(resp);
        }

        private static async Task<JsonNode> ParseAsync(HttpResponseMessage resp)
        {
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                string hint = (int)resp.StatusCode == 401
                    ? " Verifică apiKey din config.json și DESKTOP_API_KEY pe server." : "";
                throw new Exception($"Server {(int)resp.StatusCode}.{hint}");
            }
            return JsonNode.Parse(json) ?? new JsonObject();
        }

        private static async Task<string> SafeRead(HttpResponseMessage resp)
        {
            try
            {
                string s = await resp.Content.ReadAsStringAsync();
                return s.Length <= 200 ? s : s.Substring(0, 200);
            }
            catch { return ""; }
        }
    }
}
