using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Flux de login stil OAuth-loopback (ca `gh auth login`):
    ///   1. Pornim un HttpListener pe http://127.0.0.1:{port} (port liber).
    ///   2. Deschidem browserul la {webUrl}/desktop-auth?port={port}.
    ///   3. Utilizatorul se loghează (Clerk); pagina redirecționează pe loopback
    ///      cu ?token=... Îl capturăm, îl salvăm criptat și afișăm o pagină de
    ///      confirmare.
    /// Nicio parolă nu trece prin add-in; secretul de semnare stă pe server.
    /// </summary>
    public static class LoginServer
    {
        /// <summary>
        /// Rulează fluxul complet. Întoarce token-ul primit sau null la timeout/anulare.
        /// </summary>
        public static async Task<string?> RunAsync(CancellationToken ct)
        {
            int port = FreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            OpenBrowser($"{Config.WebUrl}/desktop-auth?port={port}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                while (true)
                {
                    Task<HttpListenerContext> ctxTask = listener.GetContextAsync();
                    Task done = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Infinite, timeout.Token));
                    if (done != ctxTask) return null; // anulat / timeout

                    HttpListenerContext ctx = await ctxTask;
                    string? token = ctx.Request.QueryString["token"];

                    Respond(ctx, token != null
                        ? "Autentificare reușită. Poți închide această filă și reveni în Revit."
                        : "Lipsește token-ul. Reîncearcă din Revit.");

                    if (!string.IsNullOrEmpty(token))
                    {
                        AuthStore.Save(token!);
                        return token;
                    }
                }
            }
            catch (OperationCanceledException) { return null; }
            catch (HttpListenerException) { return null; }
            finally { try { listener.Stop(); } catch { } }
        }

        private static int FreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        private static void Respond(HttpListenerContext ctx, string message)
        {
            string html =
                "<!doctype html><html><head><meta charset='utf-8'><title>Normalign</title>" +
                "<style>body{background:#0f0f11;color:#e8e6e1;font:15px system-ui;height:100vh;margin:0;" +
                "display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px}" +
                ".m{font:600 26px Georgia,serif}.m span{color:#5e6ad2}</style></head><body>" +
                "<div class='m'><span>&#10039;</span> Normalign</div>" +
                $"<div style='color:#9d9d9d'>{message}</div></body></html>";
            byte[] buf = Encoding.UTF8.GetBytes(html);
            try
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        private static void OpenBrowser(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }
}
