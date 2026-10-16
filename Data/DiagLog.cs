using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Hosting;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;

namespace BillerRestoApi.Data
{
    /// <summary>
    /// Minimal server log: one JSON object per line in api-yyyyMMdd.log.
    /// - Folder: by default ~/App_Data/logs of the deployed API (created automatically). An optional app setting
    ///   "Logging:Directory" may override it; if that is not writable the logger falls back to ~/App_Data/logs.
    ///   Nothing outside the API folder is ever used. If no folder is writable the failure is reported safely
    ///   (Trace + one Application event-log entry) and the API keeps running. The folder in use is in the app.start line.
    /// - Bounded: 10 MB per file, 3 parts per day, 30-day retention (Logging:RetentionDays), 200 MB total (Logging:MaxTotalMB).
    /// - Never throws and never changes a result: every public method swallows its own failures, so logging can
    ///   never hide the original exception.
    /// - Never logs request bodies, passwords, tokens, signing keys or connection strings. Strings are stripped of
    ///   control characters (no forged lines), credential-looking text and JWTs are masked, and everything is truncated.
    /// </summary>
    public static class DiagLog
    {
        internal static long MaxFileBytes = 10L * 1024 * 1024;   // per part; up to MaxPartsPerDay parts per UTC day
        private const int MaxPartsPerDay = 3;
        internal static bool ReportToEventLog = true;             // tests switch this off
        private static DateTime _lastPurgeDay = DateTime.MinValue;
        private const int MaxValueChars = 200;
        private const int MaxMessageChars = 300;
        private const int MaxStackChars = 1500;
        private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

        private static readonly object Gate = new object();
        private static string _directory;
        private static string _directoryError;
        private static string _directorySource;
        private static DateTime _nextResolveUtc = DateTime.MinValue;
        private static readonly HashSet<string> BadDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Test seams (internal; visible only to the unit-test assembly).
        internal static string DirectoryOverrideForTests;
        internal static bool FallbacksDisabledForTests;

        private static readonly Regex Credential = new Regex(
            @"(?i)\b(pwd|password|passwd|secret|token|apikey|signingkey)\s*=\s*[^;,\s]+", RegexOptions.Compiled);
        private static readonly Regex Jwt = new Regex(
            @"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*", RegexOptions.Compiled);
        private static readonly Regex ValidCid = new Regex("^[A-Za-z0-9\\-]{8,64}$", RegexOptions.Compiled);

        // MySQL driver messages for these errors can echo row values: keep only the numeric code.
        private static readonly HashSet<int> ValueEchoingMySqlErrors =
            new HashSet<int> { 1062, 1264, 1265, 1292, 1366, 1406, 1451, 1452 };

        public static string NewCorrelationId() { return Guid.NewGuid().ToString("N").Substring(0, 16); }

        public static bool IsValidCorrelationId(string id) { return id != null && ValidCid.IsMatch(id); }

        // ------------------------------------------------------------------ writing

        public static void Info(string area, string evt, string cid, string detail = null)
        {
            WriteException("INFO", area, evt, cid, null, null, detail);
        }

        public static void Warn(string area, string evt, string cid, string detail = null)
        {
            WriteException("WARN", area, evt, cid, null, null, detail);
        }

        /// <summary>Logs an exception once (type, scrubbed message, inner exception, stack). Never throws.</summary>
        public static void Error(string area, string evt, string cid, Exception ex, string stage = null, string detail = null)
        {
            WriteException("ERROR", area, evt, cid, ex, stage, detail);
        }

        /// <summary>Writes one structured event. String values are sanitised and truncated. Never throws.</summary>
        public static void Event(string level, string evt, string cid, IDictionary<string, object> fields)
        {
            try
            {
                var o = new JObject
                {
                    { "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "level", Token(level, 8) },
                    { "evt", Token(evt, 48) }
                };
                if (IsValidCorrelationId(cid)) o["cid"] = cid;
                if (fields != null)
                {
                    foreach (var kv in fields)
                    {
                        if (kv.Value == null) continue;
                        string key = Token(kv.Key, 32);
                        if (key.Length == 0 || o[key] != null) continue;
                        object v = kv.Value;
                        if (v is bool) o[key] = (bool)v;
                        else if (v is int || v is long || v is short || v is byte) o[key] = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                        else if (v is double || v is float || v is decimal) o[key] = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                        else o[key] = Sanitize(Convert.ToString(v, CultureInfo.InvariantCulture), MaxValueChars);
                    }
                }
                Write(o.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch { }
        }

        public static Dictionary<string, object> F(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                d[Convert.ToString(pairs[i], CultureInfo.InvariantCulture)] = pairs[i + 1];
            return d;
        }

        // ------------------------------------------------------------------ state

        /// <summary>Null when a log folder is usable; otherwise a short reason (no paths or secrets).</summary>
        public static string DirectoryProblem { get { lock (Gate) { EnsureDirectory(); return _directoryError; } } }

        /// <summary>The folder in use, or null.</summary>
        public static string ResolvedDirectory { get { lock (Gate) { EnsureDirectory(); return _directory; } } }

        /// <summary>app_data (default, inside the API folder) | configured (Logging:Directory); null when none is usable.</summary>
        public static string DirectorySource { get { lock (Gate) { EnsureDirectory(); return _directorySource; } } }

        /// <summary>Writes are synchronous, so there is never anything pending. Kept for callers/tests.</summary>
        public static bool Flush(int timeoutMs = 0) { return true; }

        /// <summary>Applies retention and the total-size cap now. Never throws.</summary>
        public static void PurgeOld()
        {
            try
            {
                string dir = ResolvedDirectory;
                if (dir != null) PurgeFolder(dir);
            }
            catch { }
        }

        /// <summary>
        /// Disk bound: (1) files older than Logging:RetentionDays (default 30) are deleted; (2) if the folder still holds
        /// more than Logging:MaxTotalMB (default 200) the OLDEST files are deleted until it fits. With 3 parts of 10 MB per
        /// day the worst case before (2) is 30 MB/day; (2) makes the folder size a hard ceiling.
        /// </summary>
        private static void PurgeFolder(string dir)
        {
            try
            {
                int days;
                if (!int.TryParse(ConfigurationManager.AppSettings["Logging:RetentionDays"], out days) || days < 1 || days > 3650) days = 30;
                int maxMb;
                if (!int.TryParse(ConfigurationManager.AppSettings["Logging:MaxTotalMB"], out maxMb) || maxMb < 10 || maxMb > 100000) maxMb = 200;

                var files = new List<string>();
                foreach (string f in Directory.GetFiles(dir, "api-*.log"))
                {
                    var m = Regex.Match(Path.GetFileName(f), @"^api-(\d{8})(-\d+)?\.log$");
                    DateTime day;
                    if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out day)) continue;
                    if (day.Date < DateTime.UtcNow.Date.AddDays(-days)) { try { File.Delete(f); } catch { } }
                    else files.Add(f);
                }

                files.Sort(StringComparer.Ordinal);   // names sort by date, then part: oldest first
                long total = 0;
                foreach (string f in files) total += new FileInfo(f).Length;
                long cap = maxMb * 1024L * 1024L;
                foreach (string f in files)
                {
                    if (total <= cap) break;
                    long len = new FileInfo(f).Length;
                    try { File.Delete(f); total -= len; } catch { }
                }
            }
            catch { }
        }

        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _directory = null; _directorySource = null; _directoryError = null;
                _nextResolveUtc = DateTime.MinValue;
                _lastPurgeDay = DateTime.MinValue;
                BadDirectories.Clear();
            }
            ReportToEventLog = false;      // tests must not write to the machine's event log
            DirectoryOverrideForTests = null;
            FallbacksDisabledForTests = false;
        }

        // ------------------------------------------------------------------ internals

        internal static string Sanitize(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = Jwt.Replace(text, "[jwt]");
            text = Credential.Replace(text, "$1=***");
            var sb = new StringBuilder(Math.Min(text.Length, max + 3));
            foreach (char c in text)
            {
                if (sb.Length >= max) { sb.Append("..."); break; }
                // Control characters and Unicode line/paragraph separators become spaces (no forged log lines).
                sb.Append(char.IsControl(c) || c == '\u2028' || c == '\u2029' || c == '\u0085' ? ' ' : c);
            }
            return sb.ToString();
        }

        private static string Token(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(Math.Min(value.Length, max));
            foreach (char c in value)
            {
                if (sb.Length >= max) break;
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '.' || c == '_' || c == ':' || c == '/' || c == '-';
                sb.Append(ok ? c : '_');
            }
            return sb.ToString();
        }

        private static void WriteException(string level, string area, string evt, string cid,
                                           Exception ex, string stage, string detail)
        {
            try
            {
                // One exception, one event: skip if this exception object was already logged at another layer.
                if (ex != null && !MarkLogged(ex)) return;

                var o = new JObject
                {
                    { "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "level", level },
                    { "area", Token(area, 32) },
                    { "evt", Token(evt, 48) }
                };
                if (IsValidCorrelationId(cid)) o["cid"] = cid;
                if (!string.IsNullOrEmpty(stage)) o["stage"] = Token(stage, 32);
                if (!string.IsNullOrEmpty(detail)) o["detail"] = Sanitize(detail, MaxMessageChars);

                if (ex != null)
                {
                    o["exType"] = ex.GetType().FullName;
                    var my = ex as MySqlException;
                    if (my != null) o["mysqlErr"] = my.Number;
                    bool echoes = my != null && ValueEchoingMySqlErrors.Contains(my.Number);
                    if (!echoes) o["msg"] = Sanitize(ex.Message, MaxMessageChars);
                    if (ex.InnerException != null)
                    {
                        o["innerType"] = ex.InnerException.GetType().FullName;
                        if (!echoes) o["innerMsg"] = Sanitize(ex.InnerException.Message, MaxMessageChars);
                    }
                    o["stack"] = Sanitize(ex.StackTrace, MaxStackChars);
                }
                Write(o.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch { }
        }

        private static bool MarkLogged(Exception ex)
        {
            try
            {
                if (ex.Data.Contains("biller.logged")) return false;
                ex.Data["biller.logged"] = true;
            }
            catch { /* Data may be read-only for some exception types: log anyway */ }
            return true;
        }

        private static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    // A failed write marks that folder bad and moves on to the next candidate; the entry is retried
                    // there instead of being lost.
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        EnsureDirectory();
                        string dir = _directory;
                        if (dir == null) break;
                        try
                        {
                            // First write of a new UTC day: apply retention and the total-size cap.
                            DateTime today = DateTime.UtcNow.Date;
                            if (_lastPurgeDay != today) { _lastPurgeDay = today; PurgeFolder(dir); }

                            // Rotation: api-yyyyMMdd.log, then -1, -2 ... when a part reaches MaxFileBytes. After MaxPartsPerDay
                            // parts the rest of the day's entries are dropped (bounded), never written without limit.
                            string stem = "api-" + today.ToString("yyyyMMdd");
                            for (int part = 0; part < MaxPartsPerDay; part++)
                            {
                                string path = Path.Combine(dir, part == 0 ? stem + ".log" : stem + "-" + part + ".log");
                                var info = new FileInfo(path);
                                if (info.Exists && info.Length >= MaxFileBytes) continue;   // this part is full: next one
                                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                                return;
                            }
                            return;   // all parts full: drop
                        }
                        catch (Exception e)
                        {
                            _directoryError = "write failed (" + e.GetType().Name + ")";
                            BadDirectories.Add(dir);
                            _directory = null;
                            _nextResolveUtc = DateTime.MinValue;
                        }
                    }
                }
                Trace.TraceError("DiagLog fallback: " + line);   // no usable folder
            }
            catch { }
        }

        // Caller holds Gate.
        private static void EnsureDirectory()
        {
            if (_directory != null || DateTime.UtcNow < _nextResolveUtc) return;

            var candidates = new List<KeyValuePair<string, string>>();
            string configured = DirectoryOverrideForTests ?? ConfigurationManager.AppSettings["Logging:Directory"];
            if (!string.IsNullOrWhiteSpace(configured))
                candidates.Add(new KeyValuePair<string, string>("configured", configured.Trim()));
            if (!FallbacksDisabledForTests)
            {
                // DEFAULT: <deployed API root>\App_Data\logs (created automatically). Nothing outside the API folder is used.
                candidates.Add(new KeyValuePair<string, string>("app_data",
                    HostingEnvironment.MapPath("~/App_Data/logs") ??
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "logs")));
            }

            string lastError = null;
            int tried = 0;
            foreach (var c in candidates)
            {
                try
                {
                    string dir = Path.GetFullPath(c.Value);
                    if (BadDirectories.Contains(dir)) continue;
                    tried++;
                    Directory.CreateDirectory(dir);

                    // Write probe: proves the app-pool identity can actually create and delete files here.
                    string probe = Path.Combine(dir, ".probe-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);

                    _directory = dir;
                    _directorySource = c.Key;
                    _directoryError = null;
                    return;
                }
                catch (Exception e)
                {
                    lastError = e.GetType().Name;
                    try { BadDirectories.Add(Path.GetFullPath(c.Value)); } catch { }
                }
            }

            _directory = null;
            _directorySource = null;
            _directoryError = "no usable log directory (" + tried + " tried" + (lastError == null ? "" : ", last: " + lastError) + ")";
            _nextResolveUtc = DateTime.UtcNow + RetryAfterFailure;
            BadDirectories.Clear();
            ReportFailure(_directoryError);
        }

        // Safe report of "no log folder is usable": Trace plus (best effort) one Windows Application-log entry using the
        // ASP.NET source that exists on every server. Contains no paths, secrets or request data. Runs at most once per
        // retry window (EnsureDirectory returns early during the back-off) and can never throw.
        private static void ReportFailure(string reason)
        {
            try
            {
                string msg = "BillerRestoApi: diagnostic log folder not writable by the app-pool identity - " + reason +
                             ". Grant Modify on <site>\\App_Data\\logs (see SECURITY_DEPLOYMENT.md).";
                Trace.TraceError(msg);
                if (ReportToEventLog)
                    EventLog.WriteEntry("ASP.NET 4.0.30319.0", msg, EventLogEntryType.Warning, 1000);
            }
            catch { }
        }
    }
}
