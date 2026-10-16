using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Web.Http;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace BillerRestoApi.UnitTests
{
    /// <summary>
    /// Runs the real Web API pipeline in memory (no IIS, no database: the DB connection string points at a closed
    /// port) and checks what authentication/authorization do and what reaches the log file.
    /// </summary>
    [TestClass]
    public class PipelineTests
    {
        private const string TestKey = "unit-test-signing-key-0123456789-abcdefghij"; // fake, test only
        private static string LogDir => Path.GetFullPath("testlogs");

        [TestInitialize]
        public void Init()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", TestKey);
            DiagLog.ResetForTests();
            if (Directory.Exists(LogDir)) Directory.Delete(LogDir, true);
            DiagLog.MaxFileBytes = 10L * 1024 * 1024;
        }

        [TestCleanup]
        public void Cleanup()
        {
            DiagLog.MaxFileBytes = 10L * 1024 * 1024;
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", null);
        }

        // ---------------- helpers ----------------

        private static List<JObject> Events()
        {
            if (!Directory.Exists(LogDir)) return new List<JObject>();
            return Directory.GetFiles(LogDir, "api-*.log").SelectMany(File.ReadAllLines)
                .Where(l => l.Length > 0).Select(JObject.Parse).ToList();
        }

        private static string RawLog()
        {
            return !Directory.Exists(LogDir) ? "" :
                string.Join("\n", Directory.GetFiles(LogDir, "api-*.log").SelectMany(File.ReadAllLines));
        }

        private static HttpClient NewClient()
        {
            var config = new HttpConfiguration();
            WebApiConfig.Register(config);
            config.EnsureInitialized();
            return new HttpClient(new HttpServer(config));
        }

        private static string Token(int id, string role)
        {
            DateTime exp;
            return TokenService.Issue(id, "user" + id, role, out exp);
        }

        private static string Forge(long exp)
        {
            Func<string, string> b64 = s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string input = b64("{\"alg\":\"HS256\",\"typ\":\"JWT\"}") + "." +
                b64("{\"iss\":\"BillerRestoApi\",\"aud\":\"ThermalPrinterTest\",\"sub\":\"5\",\"name\":\"x\",\"role\":\"User\",\"exp\":" + exp + "}");
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(TestKey)))
                return input + "." + Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(input))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static HttpRequestMessage Get(string url, string token = null, string cid = null)
        {
            var r = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + url);
            if (token != null) r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            if (cid != null) r.Headers.TryAddWithoutValidation("X-Correlation-Id", cid);
            return r;
        }

        private static HttpRequestMessage PostCategory(string token)
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/categories")
            {
                Content = new StringContent("{\"name\":\"UnitTestCategory\"}", Encoding.UTF8, "application/json")
            };
            r.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            return r;
        }

        // ---------------- authentication and authorization are still enforced ----------------

        [TestMethod]
        public void NoToken_Returns401_WithCorrelationHeader()
        {
            using (var c = NewClient())
            {
                var r = c.SendAsync(Get("/api/categories")).Result;
                Assert.AreEqual(HttpStatusCode.Unauthorized, r.StatusCode);
                Assert.IsTrue(DiagLog.IsValidCorrelationId(r.Headers.GetValues("X-Correlation-Id").Single()));
            }
        }

        [TestMethod]
        public void MalformedTamperedAndExpiredTokens_AreRejected_AndNeverLogged()
        {
            using (var c = NewClient())
            {
                string good = Token(5, "User");
                string tampered = good.Substring(0, good.Length - 3) + (good.EndsWith("AAA") ? "BBB" : "AAA");
                var bad = new[] { "not-a-jwt", tampered, Forge(DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()) };
                foreach (string t in bad)
                    Assert.AreEqual(HttpStatusCode.Unauthorized, c.SendAsync(Get("/api/categories", t)).Result.StatusCode);

                string raw = RawLog();
                foreach (string t in bad) Assert.IsFalse(raw.Contains(t), "token text leaked into the log");
            }
        }

        [TestMethod]
        public void OrdinaryUser_AdminOperation_Returns403()
        {
            using (var c = NewClient())
                Assert.AreEqual(HttpStatusCode.Forbidden, c.SendAsync(PostCategory(Token(7, "User"))).Result.StatusCode);
        }

        // ---------------- correlation id, safe errors, request log ----------------

        [TestMethod]
        public void CorrelationId_ValidIncomingIsEchoed_InvalidIsReplaced()
        {
            using (var c = NewClient())
            {
                var ok = c.SendAsync(Get("/api/categories", null, "abc-12345678")).Result;
                Assert.AreEqual("abc-12345678", ok.Headers.GetValues("X-Correlation-Id").Single());

                var bad = c.SendAsync(Get("/api/categories", null, "x y\"}{evil")).Result;
                string replaced = bad.Headers.GetValues("X-Correlation-Id").Single();
                Assert.IsTrue(DiagLog.IsValidCorrelationId(replaced));
                Assert.AreNotEqual("x y\"}{evil", replaced);
            }
        }

        [TestMethod]
        public void DatabaseFailure_Returns500_WithSafeBodyAndMatchingId_AndIsNotHiddenAsSuccess()
        {
            using (var c = NewClient())
            {
                var r = c.SendAsync(PostCategory(Token(1, "Admin"))).Result;
                Assert.AreEqual(HttpStatusCode.InternalServerError, r.StatusCode);   // failure stays a failure
                string cid = r.Headers.GetValues("X-Correlation-Id").Single();

                string body = r.Content.ReadAsStringAsync().Result;
                Assert.AreEqual(cid, (string)JObject.Parse(body)["CorrelationId"]);
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(
                    body, "(?i)stack|exception|mysql|unit_test|127\\.0\\.0\\.1|at BillerRestoApi"), body);

                var req = Events().Single(e => (string)e["evt"] == "http.request");
                Assert.AreEqual(500, (int)req["status"]);
                Assert.AreEqual(cid, (string)req["cid"]);
                Assert.AreEqual("/api/categories", (string)req["route"]);
                Assert.AreEqual(1, (int)req["userId"]);

                // The underlying exception was recorded once with the same correlation id.
                var ex = Events().Where(e => (string)e["evt"] == "exception").ToList();
                Assert.AreEqual(1, ex.Count);
                Assert.AreEqual(cid, (string)ex[0]["cid"]);

                Assert.IsFalse(RawLog().Contains("UnitTestCategory"), "request body must not be logged");
            }
        }

        [TestMethod]
        public void HealthyHealthProbe_IsNotLogged_ButFailureIs()
        {
            using (var c = NewClient())
            {
                var r = c.SendAsync(Get("/api/health/database")).Result;        // DB unreachable here
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
                Assert.IsTrue(Events().Any(e => (string)e["evt"] == "http.request" && (int)e["status"] == 503));
            }
        }

        [TestMethod]
        public void LoggingDestinationFailure_DoesNotChangeResponses()
        {
            using (var c = NewClient())
            {
                var before = c.SendAsync(Get("/api/categories")).Result;
                string blocker = Path.Combine(Path.GetTempPath(), "difflog-blocker-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(blocker, "x");
                try
                {
                    DiagLog.ResetForTests();
                    DiagLog.DirectoryOverrideForTests = Path.Combine(blocker, "sub");
                    DiagLog.FallbacksDisabledForTests = true;   // nothing usable: every log write fails

                    var during = c.SendAsync(Get("/api/categories")).Result;
                    var admin = c.SendAsync(PostCategory(Token(1, "Admin"))).Result;

                    Assert.AreEqual(before.StatusCode, during.StatusCode);
                    Assert.AreEqual(HttpStatusCode.InternalServerError, admin.StatusCode);   // same business result as with logging
                    Assert.IsNotNull(DiagLog.DirectoryProblem);
                }
                finally { File.Delete(blocker); }
            }
        }

        // ---------------- log hygiene ----------------

        [TestMethod]
        public void Event_ControlCharsAndNewlines_CannotForgeEntries()
        {
            string evil = "x\r\n{\"ts\":\"2000\",\"evt\":\"login.stages\",\"outcome\":\"success\"}\u2028\u0085\u0000";
            DiagLog.Event("INFO", "test.forge\nlogin.stages", "abcdef123456", DiagLog.F("reason", evil, "route", "/api/" + evil));
            DiagLog.Error("T", "exception", "abcdef123456", new Exception("a\nb\r\n{\"evt\":\"forged\"}\u2028c"));

            string[] lines = RawLog().Split('\n').Where(l => l.Length > 0).ToArray();
            Assert.AreEqual(2, lines.Length, "each event must stay on exactly one line");
            Assert.IsTrue(Events().All(e => (string)e["evt"] != "login.stages"));
        }

        [TestMethod]
        public void Event_OversizedValues_AreTruncated_AndSecretsAreMasked()
        {
            string big = new string('a', 100000);
            DiagLog.Event("INFO", big, "abcdef123456", DiagLog.F("reason", big));
            DiagLog.Event("INFO", "t.secret", "abcdef123456",
                DiagLog.F("reason", "Pwd=Hunter2;token=eyJhbGciOi.eyJzdWIi.c2ln password=abc"));
            string raw = RawLog();
            Assert.IsTrue(raw.Length < 1500, "log line not bounded: " + raw.Length);
            Assert.IsFalse(raw.Contains("Hunter2"));
            Assert.IsFalse(raw.Contains("c2ln"));
            Assert.IsFalse(raw.Contains("password=abc"));
        }

        [TestMethod]
        public void Rotation_RollsToNumberedParts_ThenDropsAtTheDailyLimit()
        {
            DiagLog.MaxFileBytes = 3000;
            for (int i = 0; i < 400; i++)
                DiagLog.Event("INFO", "rotating", "abcdef123456", DiagLog.F("count", i));

            var files = Directory.GetFiles(LogDir, "api-*.log").Select(Path.GetFileName).OrderBy(n => n).ToList();
            string stem = "api-" + DateTime.UtcNow.ToString("yyyyMMdd");
            CollectionAssert.AreEqual(new[] { stem + "-1.log", stem + "-2.log", stem + ".log" }.OrderBy(n => n).ToList(), files,
                "exactly 3 parts per day: base, -1, -2");
            long size = files.Sum(n => new FileInfo(Path.Combine(LogDir, n)).Length);
            Assert.IsTrue(size < 3 * (3000 + 400), "daily total grew past 3 parts: " + size);
        }

        [TestMethod]
        public void Retention_RemovesOldFiles_IncludingParts_KeepsRecentOnes()
        {
            Directory.CreateDirectory(LogDir);
            DiagLog.ResetForTests();
            Func<int, string, string> name = (daysAgo, part) =>
                Path.Combine(LogDir, "api-" + DateTime.UtcNow.AddDays(-daysAgo).ToString("yyyyMMdd") + part + ".log");
            File.WriteAllText(name(40, ""), "old");
            File.WriteAllText(name(40, "-1"), "old part");
            File.WriteAllText(name(3, ""), "recent");
            File.WriteAllText(name(3, "-1"), "recent part");
            DiagLog.PurgeOld();
            Assert.IsFalse(File.Exists(name(40, "")));
            Assert.IsFalse(File.Exists(name(40, "-1")));
            Assert.IsTrue(File.Exists(name(3, "")));
            Assert.IsTrue(File.Exists(name(3, "-1")));
        }

        [TestMethod]
        public void TotalSizeCap_DeletesOldestFirst()
        {
            Directory.CreateDirectory(LogDir);
            DiagLog.ResetForTests();
            // test app.config: Logging:MaxTotalMB = 10
            Func<int, string> name = d => Path.Combine(LogDir, "api-" + DateTime.UtcNow.AddDays(-d).ToString("yyyyMMdd") + ".log");
            foreach (int d in new[] { 5, 4, 3 })
                using (var fs = new FileStream(name(d), FileMode.Create)) fs.SetLength(6L * 1024 * 1024);   // 18 MB > 10 MB
            DiagLog.PurgeOld();
            long total = Directory.GetFiles(LogDir, "api-*.log").Sum(f => new FileInfo(f).Length);
            Assert.IsTrue(total <= 10L * 1024 * 1024, "total " + total);
            Assert.IsFalse(File.Exists(name(5)), "oldest goes first");
            Assert.IsTrue(File.Exists(name(3)), "newest stays");
        }

        [TestMethod]
        public void DefaultFolder_IsAppDataLogs_CreatedAutomatically_AndNothingElseIsUsed()
        {
            string expected = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "logs"));
            if (Directory.Exists(expected)) Directory.Delete(expected, true);
            try
            {
                DiagLog.ResetForTests();
                DiagLog.DirectoryOverrideForTests = "";           // no Logging:Directory configured
                Assert.IsFalse(Directory.Exists(expected));

                DiagLog.Event("INFO", "default.folder", "abcdef123456", DiagLog.F("outcome", "ok"));

                Assert.AreEqual("app_data", DiagLog.DirectorySource);
                Assert.AreEqual(expected, DiagLog.ResolvedDirectory, true);
                Assert.IsTrue(Directory.Exists(expected), "folder is created automatically");
                StringAssert.Contains(string.Join("\n", Directory.GetFiles(expected, "api-*.log").SelectMany(File.ReadAllLines)), "default.folder");
            }
            finally { DiagLog.ResetForTests(); try { Directory.Delete(expected, true); } catch { } }
        }

        [TestMethod]
        public void NoUsableFolder_IsReportedSafely_AndNeverThrows()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "difflog-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            try
            {
                DiagLog.DirectoryOverrideForTests = Path.Combine(blocker, "sub");
                DiagLog.FallbacksDisabledForTests = true;
                DiagLog.Error("T", "exception", "abcdef123456", new InvalidOperationException("original"));   // must not throw
                string problem = DiagLog.DirectoryProblem;
                Assert.IsNotNull(problem);
                Assert.IsFalse(problem.Contains(blocker), "the report must not expose paths");
            }
            finally { File.Delete(blocker); }
        }

        [TestMethod]
        public void Exception_IsLoggedOnce_AcrossLayers()
        {
            var ex = new InvalidOperationException("once");
            Db.LogError("Items", ex);                                    // controller layer
            DiagLog.Error("Unhandled", "exception", "abcdef123456", ex); // framework layer: same object
            Assert.AreEqual(1, Events().Count(e => (string)e["exType"] == "System.InvalidOperationException"));
        }

        [TestMethod]
        public void MySqlValueEchoingErrors_DoNotLogTheDriverMessage_ButSchemaErrorsDo()
        {
            Func<string, int, MySql.Data.MySqlClient.MySqlException> make = (msg, no) =>
                (MySql.Data.MySqlClient.MySqlException)Activator.CreateInstance(
                    typeof(MySql.Data.MySqlClient.MySqlException),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, new object[] { msg, no }, null);

            DiagLog.Error("T", "exception", "abcdef123456", make("Duplicate entry 'Jane Doe 9876543210' for key 'x'", 1062));
            Assert.IsFalse(RawLog().Contains("9876543210"), "value-echoing driver message was logged");
            Assert.IsTrue(RawLog().Contains("\"mysqlErr\":1062"));

            DiagLog.Error("T", "exception", "abcdef123456", make("Unknown column 'lastLogin' in 'field list'", 1054));
            StringAssert.Contains(RawLog(), "Unknown column");   // the text that makes a schema problem diagnosable
        }
    }
}
