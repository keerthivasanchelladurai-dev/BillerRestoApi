using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using BillerRestoApi.Data;

namespace BillerRestoApi.Security
{
    /// <summary>
    /// Outermost Web API handler. For every request it adopts a valid incoming X-Correlation-Id (else generates one),
    /// returns it in the X-Correlation-Id response header, gives an EMPTY 5xx response a generic JSON body
    /// {Message, CorrelationId} (never exception details) and writes one "http.request" line (method, normalised
    /// route, status, duration, user id). Bodies, query strings, headers and usernames are never read or logged;
    /// healthy health-check probes are not logged. A logging problem can never alter the response.
    /// </summary>
    public class RequestLoggingHandler : DelegatingHandler
    {
        private static readonly Regex Digits = new Regex("^[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex SafeSegment = new Regex("^[a-z0-9_\\-]{1,32}$", RegexOptions.Compiled);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string cid = Correlation.FromRequest(request);
            Correlation.Set(cid);

            var sw = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch
            {
                sw.Stop();
                Log(request, null, sw.ElapsedMilliseconds, cid);
                throw;
            }

            sw.Stop();
            try
            {
                if (response.StatusCode >= HttpStatusCode.InternalServerError) EnsureErrorBody(response, cid);
                response.Headers.Remove(Correlation.HeaderName);
                response.Headers.TryAddWithoutValidation(Correlation.HeaderName, cid);
            }
            catch { }

            Log(request, response, sw.ElapsedMilliseconds, cid);
            return response;
        }

        private static void EnsureErrorBody(HttpResponseMessage response, string cid)
        {
            // Only fill genuinely empty bodies (e.g. InternalServerError()). Existing generic bodies are untouched.
            if (response.Content == null || response.Content.Headers.ContentLength == 0)
            {
                response.Content = new StringContent(
                    "{\"Message\":\"An unexpected error occurred.\",\"CorrelationId\":\"" + cid + "\"}",
                    Encoding.UTF8, "application/json");
            }
        }

        private static void Log(HttpRequestMessage request, HttpResponseMessage response, long ms, string cid)
        {
            try
            {
                int status = response == null ? 500 : (int)response.StatusCode;
                string route = NormalizeRoute(request.RequestUri);
                if (status == 200 && route == "/api/health/database") return;

                var f = DiagLog.F("method", request.Method == null ? "?" : request.Method.Method,
                                  "route", route, "status", status, "durationMs", ms);
                var ctx = request.GetRequestContext();
                var id = ctx == null ? null : ctx.Principal == null ? null : ctx.Principal.Identity as ClaimsIdentity;
                var sub = id == null || !id.IsAuthenticated ? null : id.FindFirst(ClaimTypes.NameIdentifier);
                if (sub != null) f["userId"] = sub.Value;

                DiagLog.Event(status >= 500 ? "ERROR" : status >= 400 ? "WARN" : "INFO", "http.request", cid, f);
            }
            catch { }
        }

        /// <summary>Path only (no query string), lower-cased; numeric segments become {id}; unusual ones {x}. Bounded.</summary>
        internal static string NormalizeRoute(Uri uri)
        {
            try
            {
                if (uri == null) return "/";
                string[] segs = uri.AbsolutePath.ToLowerInvariant().Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                int n = 0;
                foreach (string s in segs)
                {
                    if (++n > 6) { sb.Append("/{x}"); break; }
                    sb.Append('/');
                    sb.Append(Digits.IsMatch(s) ? "{id}" : (SafeSegment.IsMatch(s) ? s : "{x}"));
                }
                return sb.Length == 0 ? "/" : sb.ToString();
            }
            catch { return "/"; }
        }
    }

    /// <summary>Logs exceptions that escape a controller (once per exception object). The response stays generic.</summary>
    public class DiagExceptionLogger : ExceptionLogger
    {
        public override void Log(ExceptionLoggerContext context)
        {
            try
            {
                if (context == null || context.Exception == null) return;
                DiagLog.Error("Unhandled", "exception", Correlation.Current, context.Exception);
            }
            catch { }
        }
    }
}
