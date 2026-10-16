using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Http;
using BillerRestoApi.Controllers;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;

namespace BillerRestoApi.UnitTests
{
    /// <summary>
    /// Drives AuthController.Login against an isolated throw-away schema (biller_ut_*) on a LOCAL test server and
    /// asserts exactly which log event / stage each failure produces. Uses only fake accounts and a fake signing
    /// key. Skipped (Inconclusive) when the local test server is not reachable. Never touches an existing schema.
    /// </summary>
    [TestClass]
    public class LoginFlowTests
    {
        private const string GoodKey = "unit-test-signing-key-0123456789-abcdefghij"; // fake, test only
        private const string Password = "Correct-Pass-1";                              // fake, test only

        private string _admin;     // server-level connection string (no database)
        private string _schema;
        private string _user;      // unique per test: keeps the in-memory throttle independent
        private static string LogDir => Path.GetFullPath("testlogs");

        [TestInitialize]
        public void Init()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", null);
            DiagLog.Flush();
            DiagLog.ResetForTests();
            if (Directory.Exists(LogDir)) Directory.Delete(LogDir, true);

            _admin = ConfigurationManager.AppSettings["Test:AdminConnection"];
            _schema = "biller_ut_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _user = "ut_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            try
            {
                Exec(_admin, "CREATE DATABASE `" + _schema + "`");
                Exec(Conn(),
                    "CREATE TABLE tbllogin (id INT AUTO_INCREMENT PRIMARY KEY, userName VARCHAR(255) NOT NULL, " +
                    "password VARCHAR(255) NULL, type VARCHAR(50) NULL, lastLogin DATETIME NULL)");
                Insert(_user, PasswordHasher.HashPassword(Password), "User");
                Db.ConnectionStringOverrideForTests = Conn();
            }
            catch (Exception e)
            {
                Db.ConnectionStringOverrideForTests = null;
                Assert.Inconclusive("Local test database not available (" + e.GetType().Name + "); login-flow tests skipped.");
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            Db.ConnectionStringOverrideForTests = null;
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", null);
            if (_admin == null || _schema == null) return;
            try { Exec(_admin, "DROP DATABASE IF EXISTS `" + _schema + "`"); } catch { }
            // Prove isolation was cleaned up: the throw-away schema must be gone after every test.
            try
            {
                Assert.AreEqual(0, CountSchemas(_admin, _schema), "test schema was not dropped");
            }
            catch (MySqlException) { /* server went away: nothing more to verify */ }
        }

        [ClassCleanup]
        public static void NoTestSchemaLeftBehind()
        {
            string admin = ConfigurationManager.AppSettings["Test:AdminConnection"];
            try { Assert.AreEqual(0, CountSchemas(admin, "biller_ut_%"), "leftover biller_ut_* schemas"); }
            catch (MySqlException) { /* server unavailable: tests were skipped */ }
        }

        private static int CountSchemas(string conn, string likePattern)
        {
            using (var c = new MySqlConnection(conn))
            {
                c.Open();
                using (var cmd = new MySqlCommand("SHOW DATABASES LIKE '" + likePattern + "'", c))
                using (var r = cmd.ExecuteReader())
                {
                    int n = 0;
                    while (r.Read()) n++;
                    return n;
                }
            }
        }

        // ---------------- helpers ----------------

        private string Conn() { return _admin + ";Database=" + _schema; }

        private static void Exec(string conn, string sql)
        {
            using (var c = new MySqlConnection(conn))
            {
                c.Open();
                using (var cmd = new MySqlCommand(sql, c)) cmd.ExecuteNonQuery();
            }
        }

        private void Insert(string name, string password, string type)
        {
            using (var c = new MySqlConnection(Conn()))
            {
                c.Open();
                using (var cmd = new MySqlCommand("INSERT INTO tbllogin (userName, password, type) VALUES (@u,@p,@t)", c))
                {
                    cmd.Parameters.AddWithValue("@u", name);
                    cmd.Parameters.AddWithValue("@p", password);
                    cmd.Parameters.AddWithValue("@t", type);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private object Scalar(string sql)
        {
            using (var c = new MySqlConnection(Conn()))
            {
                c.Open();
                using (var cmd = new MySqlCommand(sql, c)) return cmd.ExecuteScalar();
            }
        }

        private class Outcome
        {
            public int Status;
            public object Body;
            public string Cid;
            public T Get<T>(string prop) { return (T)Body.GetType().GetProperty(prop).GetValue(Body); }
        }

        private static Outcome Login(string user, string password)
        {
            var result = new AuthController().Login(new AuthController.LoginRequest { Username = user, Password = password });
            var t = result.GetType();
            var o = new Outcome();
            if (t.Name.StartsWith("OkNegotiated")) o.Status = 200;
            else if (t.Name.StartsWith("NegotiatedContent")) o.Status = (int)(HttpStatusCode)t.GetProperty("StatusCode").GetValue(result);
            else throw new AssertFailedException("unexpected result type " + t.Name);
            o.Body = t.GetProperty("Content").GetValue(result);
            var cidProp = o.Body.GetType().GetProperty("CorrelationId");
            o.Cid = cidProp == null ? null : (string)cidProp.GetValue(o.Body);
            return o;
        }

        private static JObject[] Events(string name)
        {
            DiagLog.Flush();
            if (!Directory.Exists(LogDir)) return new JObject[0];
            return Directory.GetFiles(LogDir, "*.log").SelectMany(File.ReadAllLines)
                .Where(l => l.Length > 0).Select(JObject.Parse)
                .Where(e => name == null || (string)e["evt"] == name).ToArray();
        }

        private static string RawLog()
        {
            DiagLog.Flush();
            return !Directory.Exists(LogDir) ? "" :
                string.Join("\n", Directory.GetFiles(LogDir, "*.log").SelectMany(File.ReadAllLines));
        }

        // ---------------- signing key: missing / short / valid ----------------

        [TestMethod]
        public void MissingKey_ValidPassword_Returns500_AndLogsDedicatedEvent_NotAnException()
        {
            // No env var, no Auth:SigningKey in the test config => exactly the production "key not deployed" case.
            var r = Login(_user, Password);

            Assert.AreEqual(500, r.Status);
            Assert.IsTrue(DiagLog.IsValidCorrelationId(r.Cid), "client gets a correlation id");
            Assert.IsNull(r.Body.GetType().GetProperty("Token"), "no token in an error body");

            var ev = Events("login.signing_key_unavailable").Single();
            Assert.AreEqual(r.Cid, (string)ev["cid"]);
            Assert.AreEqual("token-issue", (string)ev["stage"]);
            Assert.AreEqual("missing", (string)ev["signingKey"], "status must not be masked by the scrubber");
            Assert.AreEqual(0, Events("login.exception").Length, "must be distinguishable from an exception");


            // Fail closed: nothing was written for a login that was not completed.
            Assert.AreEqual(DBNull.Value, Scalar("SELECT lastLogin FROM tbllogin WHERE userName='" + _user + "'"));
        }

        [TestMethod]
        public void ShortKey_ValidPassword_Returns500_AndReportsTooShort()
        {
            // Boundary: 31 characters is rejected, 32 is accepted (AuthSettings.MinKeyLength).
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", new string('k', 32));
            Assert.AreEqual("ok-env", AuthSettings.GetSigningKeyStatus());
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", "only-31-characters-long-key-xxx");
            Assert.AreEqual(31, Environment.GetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY").Length);

            var r = Login(_user, Password);

            Assert.AreEqual(500, r.Status);
            var ev = Events("login.signing_key_unavailable").Single();
            Assert.AreEqual("too-short-env", (string)ev["signingKey"]);
            Assert.AreEqual(r.Cid, (string)ev["cid"]);
            Assert.IsFalse(RawLog().Contains("only-31-characters"), "key material must never be logged");
        }

        [TestMethod]
        public void ValidKey_ValidPassword_ReturnsValidatableToken_AndCompletesAllStages()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);

            var r = Login(_user, Password);

            Assert.AreEqual(200, r.Status);
            Assert.IsTrue(r.Get<bool>("Success"));
            string token = r.Get<string>("Token");
            var principal = TokenService.Validate(token);
            Assert.IsNotNull(principal);
            Assert.AreEqual(r.Get<int?>("UserId").ToString(), principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier).Value);
            Assert.AreEqual("User", principal.FindFirst(System.Security.Claims.ClaimTypes.Role).Value);

            Assert.AreNotEqual(DBNull.Value, Scalar("SELECT lastLogin FROM tbllogin WHERE userName='" + _user + "'"));

            Assert.AreEqual(0, Events("login.exception").Length + Events("login.signing_key_unavailable").Length);
            Assert.IsFalse(RawLog().Contains(token), "token must never be logged");
        }

        // ---------------- password verification ----------------

        [TestMethod]
        public void WrongPassword_And_UnknownUser_AreIndistinguishableToTheClient()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);

            var wrong = Login(_user, "Wrong-Pass-9");
            var unknown = Login("no_such_" + _user, "Wrong-Pass-9");

            Assert.AreEqual(wrong.Status, unknown.Status);
            Assert.AreEqual(200, wrong.Status);
            Assert.IsFalse(wrong.Get<bool>("Success"));
            Assert.IsFalse(unknown.Get<bool>("Success"));
            Assert.AreEqual(wrong.Get<string>("Message"), unknown.Get<string>("Message"));
            Assert.IsNull(wrong.Get<string>("Token"));
            Assert.IsNull(unknown.Get<string>("Token"));
            Assert.IsNull(wrong.Get<int?>("UserId"), "no user id in the response");
            Assert.IsNull(unknown.Get<int?>("UserId"));

            // Same generic outcome for both; neither is an exception or a key problem.
            var stages = Events("login.stages");
            Assert.AreEqual(2, stages.Length);
            Assert.IsTrue(stages.All(e => (string)e["outcome"] == "invalid_credentials"));
            Assert.AreEqual(0, Events("login.exception").Length);
            Assert.IsFalse(RawLog().Contains(_user), "usernames must not be logged");
            Assert.IsFalse(RawLog().Contains("Wrong-Pass-9"));
        }

        [TestMethod]
        public void LegacyNonPbkdf2Password_IsAnOrdinaryFailure_NotA500()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            string legacy = "legacy_" + _user;
            Insert(legacy, "plain-or-md5-legacy-value", "User");

            var r = Login(legacy, "plain-or-md5-legacy-value");

            Assert.AreEqual(200, r.Status);
            Assert.IsFalse(r.Get<bool>("Success"));
            Assert.AreEqual(0, Events("login.exception").Length);
        }

        // ---------------- database stages ----------------

        [TestMethod]
        public void LastLoginUpdateFailure_IsLoggedAtItsOwnStage_AndNeverAsSuccess()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            Exec(Conn(), "ALTER TABLE tbllogin DROP COLUMN lastLogin");     // the production schema-difference hypothesis

            var r = Login(_user, Password);

            Assert.AreEqual(500, r.Status);
            var ev = Events("login.exception").Single();
            Assert.AreEqual("last-login-update", (string)ev["stage"]);
            Assert.AreEqual(1054, (int)ev["mysqlErr"]);                      // unknown column
            StringAssert.Contains((string)ev["msg"], "lastLogin");
            Assert.AreEqual(r.Cid, (string)ev["cid"]);
            Assert.AreEqual(0, Events("login.signing_key_unavailable").Length);
            Assert.AreEqual("exception", (string)Events("login.stages").Single()["outcome"], "a login that returned 500 is not a success");
            Assert.IsNull(r.Body.GetType().GetProperty("Token"), "the issued token must not leak out of a failed login");
        }

        [TestMethod]
        public void UserLookupSchemaFailure_IsLoggedAtUserLookupStage()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            Exec(Conn(), "ALTER TABLE tbllogin DROP COLUMN type");

            var r = Login(_user, Password);

            Assert.AreEqual(500, r.Status);
            var ev = Events("login.exception").Single();
            Assert.AreEqual("user-lookup", (string)ev["stage"]);
            Assert.AreEqual(1054, (int)ev["mysqlErr"]);
        }

        [TestMethod]
        public void DatabaseConnectFailure_IsLoggedAtDbConnectStage()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            Db.ConnectionStringOverrideForTests = "Server=127.0.0.1;Port=1;Database=x;Uid=u;Pwd=UnitTestOnlyPw-9f3a;Connection Timeout=2;";

            var r = Login(_user, Password);

            Assert.AreEqual(500, r.Status);
            Assert.AreEqual("db-connect", (string)Events("login.exception").Single()["stage"]);
            Assert.IsFalse(RawLog().Contains("UnitTestOnlyPw-9f3a"));
        }

        // ---------------- per-stage trace: every stage, same correlation id as the response ----------------

        private const string AllStages =
            "throttle:ok/db-connect:ok/user-lookup:ok/password-verify:ok/token-issue:ok/last-login-update:ok/respond:ok";

        [TestMethod]
        public void SuccessfulLogin_RecordsEveryStage_WithoutLeakingSecrets()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            var r = Login(_user, Password);
            Assert.AreEqual(200, r.Status);

            var s = Events("login.stages").Single();
            Assert.AreEqual(AllStages, (string)s["stages"]);
            Assert.AreEqual("success", (string)s["outcome"]);
            Assert.AreEqual("INFO", (string)s["level"]);
            Assert.AreEqual(r.Get<int?>("UserId"), (int?)s["userId"], "safe identifier = internal user id");
            Assert.IsTrue(DiagLog.IsValidCorrelationId((string)s["cid"]));

            string raw = RawLog();
            Assert.IsFalse(raw.Contains(Password));
            Assert.IsFalse(raw.Contains(r.Get<string>("Token")));
            Assert.IsFalse(raw.Contains(GoodKey));
            Assert.IsFalse(raw.Contains(_user), "submitted username is not logged");
            Assert.IsFalse(raw.Contains("Pwd=") || raw.Contains("Uid="), "no connection string fragments");
        }

        [TestMethod]
        public void MissingKey_StageTrace_StopsAtTokenIssue_WithTheResponseCorrelationId()
        {
            var r = Login(_user, Password);
            var s = Events("login.stages").Single();
            Assert.AreEqual("throttle:ok/db-connect:ok/user-lookup:ok/password-verify:ok/token-issue:FAIL", (string)s["stages"]);
            Assert.AreEqual("key_unavailable", (string)s["outcome"]);
            Assert.AreEqual("ERROR", (string)s["level"]);
            Assert.AreEqual(r.Cid, (string)s["cid"]);
        }

        [TestMethod]
        public void LastLoginFailure_StageTrace_ShowsTokenIssuedThenLastLoginFailed()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            Exec(Conn(), "ALTER TABLE tbllogin DROP COLUMN lastLogin");
            var r = Login(_user, Password);

            var s = Events("login.stages").Single();
            Assert.AreEqual("throttle:ok/db-connect:ok/user-lookup:ok/password-verify:ok/token-issue:ok/last-login-update:FAIL", (string)s["stages"]);
            Assert.AreEqual("exception", (string)s["outcome"]);
            Assert.AreEqual(r.Cid, (string)s["cid"]);
            Assert.AreEqual(r.Cid, (string)Events("login.exception").Single()["cid"], "all stage events share the response id");

        }

        [TestMethod]
        public void InvalidCredentials_StageTrace_ReturnsExistingResponse_AndHasNoUserIdForUnknownUser()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            var wrong = Login(_user, "Wrong-Pass-9");
            var unknown = Login("no_such_" + _user, "Wrong-Pass-9");

            Assert.AreEqual(200, wrong.Status);
            Assert.AreEqual(200, unknown.Status);
            Assert.AreEqual("Invalid username or password.", wrong.Get<string>("Message"));
            Assert.AreEqual("Invalid username or password.", unknown.Get<string>("Message"));

            var stages = Events("login.stages").ToArray();
            Assert.AreEqual(2, stages.Length);
            Assert.AreEqual("throttle:ok/db-connect:ok/user-lookup:ok/password-verify:ok", (string)stages[0]["stages"]);
            Assert.IsNotNull(stages[0]["userId"]);
            Assert.AreEqual("throttle:ok/db-connect:ok/user-lookup:ok", (string)stages[1]["stages"]);
            Assert.IsNull(stages[1]["userId"]);
            Assert.IsTrue(stages.All(e => (string)e["outcome"] == "invalid_credentials" && (string)e["level"] == "INFO"));
        }

        [TestMethod]
        public void LoggingFailure_DoesNotMaskTheOriginalLoginFailure()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            Exec(Conn(), "ALTER TABLE tbllogin DROP COLUMN lastLogin");

            string blocker = Path.Combine(Path.GetTempPath(), "difflog-blocker-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(blocker, "x");
            try
            {
                DiagLog.DirectoryOverrideForTests = Path.Combine(blocker, "sub");   // every log write fails
                DiagLog.FallbacksDisabledForTests = true;

                var r = Login(_user, Password);          // must still be the ORIGINAL failure, not a logging error

                Assert.AreEqual(500, r.Status);
                Assert.IsTrue(DiagLog.IsValidCorrelationId(r.Cid));
                StringAssert.Contains((string)r.Body.GetType().GetProperty("Message").GetValue(r.Body), "server error");
                DiagLog.Flush();
                Assert.IsNotNull(DiagLog.DirectoryProblem);
            }
            finally { File.Delete(blocker); }
        }

        // ---------------- JWT issuance (no database needed) ----------------

        [TestMethod]
        public void Issue_WithoutKey_ReturnsNull_WithKey_ReturnsValidToken_ShortKey_ReturnsNull()
        {
            DateTime exp;
            Assert.IsNull(TokenService.Issue(1, "u", "User", out exp));

            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", "short");
            Assert.IsNull(TokenService.Issue(1, "u", "User", out exp));

            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            string t = TokenService.Issue(7, "u", "admin", out exp);
            Assert.IsNotNull(t);
            Assert.AreEqual(3, t.Split('.').Length);
            Assert.IsTrue(exp > DateTime.UtcNow);
            var p = TokenService.Validate(t);
            Assert.AreEqual("Admin", p.FindFirst(System.Security.Claims.ClaimTypes.Role).Value);   // NormalizeRole
        }

        [TestMethod]
        public void LoginStages_CarryPerStageTimings_AndStartupCorrelationFields_WithoutSecrets()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            var ok = Login(_user, Password);
            var unknown = Login("no_such_" + _user, "Wrong-Pass-9");
            Assert.AreEqual(200, ok.Status);
            Assert.AreEqual(200, unknown.Status);

            var s = Events("login.stages").ToArray();
            Assert.AreEqual(2, s.Length);

            // Successful login: every timed stage is present, as "name:ms" (never "name=ms").
            string ms = (string)s[0]["stageMs"];
            foreach (string stage in new[] { "db-connect", "user-lookup", "password-verify", "token-issue", "last-login-update" })
                StringAssert.Contains(ms, stage + ":");
            Assert.IsFalse(ms.Contains("="));
            Assert.IsTrue((long)s[0]["totalMs"] >= 0);
            Assert.IsTrue((long)s[0]["sinceStartMs"] >= 0);
            Assert.IsTrue((int)s[0]["loginNo"] >= 1);
            Assert.IsFalse(string.IsNullOrEmpty((string)s[0]["build"]));
            Assert.IsTrue((string)s[0]["pbkdf2"] == "native-cng" || (string)s[0]["pbkdf2"] == "managed");

            // The stages string is unchanged; for an unknown user the dummy hash is reported on its own.
            Assert.AreEqual(AllStages, (string)s[0]["stages"]);
            Assert.AreEqual("throttle:ok/db-connect:ok/user-lookup:ok", (string)s[1]["stages"]);
            StringAssert.Contains((string)s[1]["stageMs"], "dummy-hash:");

            // Nothing secret reaches the log.
            string raw = RawLog();
            Assert.IsFalse(raw.Contains(Password));
            Assert.IsFalse(raw.Contains("Wrong-Pass-9"));
            Assert.IsFalse(raw.Contains(GoodKey));
            Assert.IsFalse(raw.Contains(_user));
        }

        [TestMethod]
        public void Issue_NullRole_DoesNotThrow()
        {
            // The only data-dependent input from the database (type can be NULL). Must not become a 500.
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            DateTime exp;
            string t = TokenService.Issue(7, "u", null, out exp);
            Assert.IsNotNull(t);
        }

        [TestMethod]
        public void Token_SignedWithADifferentKey_IsRejected()
        {
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", GoodKey);
            DateTime exp;
            string t = TokenService.Issue(7, "u", "User", out exp);
            Environment.SetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY", "another-unit-test-key-0123456789-zzzzzzzz");
            Assert.IsNull(TokenService.Validate(t));
        }
    }
}
