using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BillerRestoApi.Controllers;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace BillerRestoApi.UnitTests
{
    [TestClass]
    public class DiagnosticsTests
    {
        private const string FakeKey = "unit-test-signing-key-0123456789-abcdefghij"; // fake, test only
        private static string LogDir => Path.GetFullPath("testlogs");

        [TestInitialize]
        public void Init()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", null);
            ResetLogger();
            if (Directory.Exists(LogDir)) Directory.Delete(LogDir, true);
        }

        private static void ResetLogger()
        {
            DiagLog.Flush();
            DiagLog.ResetForTests();
        }

        private static string[] ReadLines()
        {
            DiagLog.Flush();
            if (!Directory.Exists(LogDir)) return new string[0];
            return Directory.GetFiles(LogDir, "api-*.log")
                .SelectMany(File.ReadAllLines).Where(l => l.Length > 0).ToArray();
        }

        // ---- signing-key status (distinguishes the failure modes without exposing the key) ----

        [TestMethod]
        public void KeyStatus_Missing_WhenNothingConfigured()
        {
            Assert.AreEqual("missing", AuthSettings.GetSigningKeyStatus());
            Assert.IsNull(AuthSettings.GetSigningKey());
        }

        [TestMethod]
        public void KeyStatus_TooShort_WhenEnvKeyUnder32Chars()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", "short-key");
            Assert.AreEqual("too-short-env", AuthSettings.GetSigningKeyStatus());
            Assert.IsNull(AuthSettings.GetSigningKey());
        }

        [TestMethod]
        public void KeyStatus_Ok_WhenEnvKeyLongEnough()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", FakeKey);
            Assert.AreEqual("ok-env", AuthSettings.GetSigningKeyStatus());
            Assert.IsNotNull(AuthSettings.GetSigningKey());
        }

        // ---- logger ----

        [TestMethod]
        public void Log_WritesStructuredLine_WithCorrelationIdStageAndExceptionType()
        {
            DiagLog.Error("Login", "login.exception", "abc12345", new InvalidOperationException("boom"), "user-lookup");
            var line = JObject.Parse(ReadLines().Single());
            Assert.AreEqual("ERROR", (string)line["level"]);
            Assert.AreEqual("abc12345", (string)line["cid"]);
            Assert.AreEqual("user-lookup", (string)line["stage"]);
            Assert.AreEqual("System.InvalidOperationException", (string)line["exType"]);
        }

        [TestMethod]
        public void Log_ScrubsCredentialsAndJwtsFromMessages()
        {
            string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJlLXZhbHVl";
            var ex = new Exception("failed Server=db;Uid=app;Pwd=Hunter2Secret;Port=3306 token=" + jwt + " password=abc");
            DiagLog.Error("Test", "exception", "c1", ex);
            string all = string.Join("\n", ReadLines());
            Assert.IsFalse(all.Contains("Hunter2Secret"), "connection-string password leaked");
            Assert.IsFalse(all.Contains("c2lnbmF0dXJlLXZhbHVl"), "JWT leaked");
            Assert.IsFalse(all.Contains("password=abc"), "password leaked");
        }

        [TestMethod]
        public void Log_TruncatesLongMessages()
        {
            DiagLog.Error("Test", "exception", "c2", new Exception(new string('x', 5000)));
            var line = JObject.Parse(ReadLines().Single());
            Assert.IsTrue(((string)line["msg"]).Length <= 310);
        }

        [TestMethod]
        public void Log_NeverThrows_WhenDirectoryIsUnusable()
        {
            // Point the logger at a path whose parent is a regular file: creating/writing must fail.
            string blocker = Path.Combine(Path.GetTempPath(), "difflog-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            try
            {
                // Only the broken candidate is allowed: nothing usable remains, so the failure must be recorded.
                DiagLog.DirectoryOverrideForTests = Path.Combine(blocker, "sub");
                DiagLog.FallbacksDisabledForTests = true;
                DiagLog.Error("Test", "exception", "c3abcdef", new Exception("x"));   // must not throw
                DiagLog.Info("Test", "evt", null);                                     // must not throw
                DiagLog.Flush();                                                        // writer is asynchronous
                Assert.IsNotNull(DiagLog.DirectoryProblem, "failure should be recorded");
                Assert.IsNull(DiagLog.DirectorySource);
            }
            finally { File.Delete(blocker); }
        }

        [TestMethod]
        public void Log_FallsBackToAnotherDirectory_WhenConfiguredOneIsNotWritable()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "difflog-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            string fallbackDir = null;
            try
            {
                DiagLog.DirectoryOverrideForTests = Path.Combine(blocker, "sub");   // configured but unusable
                DiagLog.Event("INFO", "fallback.probe", "abcdef123456", DiagLog.F("outcome", "ok"));
                DiagLog.Flush();

                Assert.IsNull(DiagLog.DirectoryProblem, "a fallback should be usable");
                Assert.AreEqual("app_data", DiagLog.DirectorySource);   // never a folder outside the API folder
                fallbackDir = DiagLog.ResolvedDirectory;
                string text = string.Join("\n", Directory.GetFiles(fallbackDir, "api-*.log").SelectMany(File.ReadAllLines));
                StringAssert.Contains(text, "fallback.probe");
            }
            finally
            {
                File.Delete(blocker);
                DiagLog.ResetForTests();
                try { if (fallbackDir != null) foreach (var f in Directory.GetFiles(fallbackDir, "api-*.log")) File.Delete(f); } catch { }
            }
        }

        [TestMethod]
        public void WebConfig_HidesAppData_AndRefusesLogFiles()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            string webConfig = null;
            for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                if (File.Exists(Path.Combine(dir, "BillerRestoApi.csproj"))) { webConfig = Path.Combine(dir, "Web.config"); break; }
            Assert.IsNotNull(webConfig, "could not locate the API project folder");

            var rf = System.Xml.Linq.XDocument.Load(webConfig).Descendants("requestFiltering").Single();
            Assert.IsTrue(rf.Element("hiddenSegments").Elements("add").Any(a => (string)a.Attribute("segment") == "App_Data"));
            Assert.IsTrue(rf.Element("fileExtensions").Elements("add")
                .Any(a => (string)a.Attribute("fileExtension") == ".log" && (string)a.Attribute("allowed") == "false"));
        }

        // ---- login: exception path returns a generic body + correlation id and logs the real stage ----

        [TestMethod]
        public void Login_DbFailure_ReturnsGeneric500WithCorrelationId_AndLogsStage()
        {
            var controller = new AuthController();
            var result = controller.Login(new AuthController.LoginRequest { Username = "nobody", Password = "Wrong-Pass-1" });

            var type = result.GetType();
            Assert.IsTrue(type.Name.StartsWith("NegotiatedContentResult"), type.Name);
            Assert.AreEqual(System.Net.HttpStatusCode.InternalServerError, type.GetProperty("StatusCode").GetValue(result));

            object body = type.GetProperty("Content").GetValue(result);
            string cid = (string)body.GetType().GetProperty("CorrelationId").GetValue(body);
            string message = (string)body.GetType().GetProperty("Message").GetValue(body);
            Assert.IsFalse(string.IsNullOrEmpty(cid));
            StringAssert.DoesNotMatch(message, new System.Text.RegularExpressions.Regex("(?i)mysql|exception|unit_test|127\\.0\\.0\\.1|stack"));

            var line = ReadLines().Select(JObject.Parse).Single(o => (string)o["evt"] == "login.exception");
            Assert.AreEqual(cid, (string)line["cid"]);
            Assert.AreEqual("db-connect", (string)line["stage"]);
            string all = string.Join("\n", ReadLines());
            Assert.IsFalse(all.Contains("UnitTestOnlyPw-9f3a"), "DB password leaked into log");
            Assert.IsFalse(all.Contains("Wrong-Pass-1"), "submitted password leaked into log");
        }
    }
}
