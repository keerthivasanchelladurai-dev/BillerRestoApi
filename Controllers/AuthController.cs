
using System;
using BillerRestoApi.Data;
using System.Web.Http;
using MySql.Data.MySqlClient;
using BillerRestoApi.Security;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class LoginResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public int? UserId { get; set; }
            public string Username { get; set; }
            public string Role { get; set; }
            public string Token { get; set; }
            public string TokenType { get; set; }
            public DateTime? ExpiresAtUtc { get; set; }
        }

        private static int _loginCount;   // logins handled by this process (1 = first since start)

        /// <summary>Request scheme as the application sees it ("http"/"https"), never anything else.</summary>
        private string SafeScheme()
        {
            try
            {
                string s = Request != null && Request.RequestUri != null ? Request.RequestUri.Scheme : null;
                return s == "https" || s == "http" ? s : "other";
            }
            catch { return "other"; }
        }

        /// <summary>X-Forwarded-Proto reduced to "https", "http", "other" or "none" (the raw header is never logged).</summary>
        private string SafeForwardedProto()
        {
            try
            {
                System.Collections.Generic.IEnumerable<string> values;
                if (Request == null || !Request.Headers.TryGetValues("X-Forwarded-Proto", out values)) return "none";
                foreach (string v in values)
                {
                    if (string.Equals(v, "https", StringComparison.OrdinalIgnoreCase)) return "https";
                    if (string.Equals(v, "http", StringComparison.OrdinalIgnoreCase)) return "http";
                }
                return "other";
            }
            catch { return "other"; }
        }

        private string GetClientIp()
        {
            var ctx = System.Web.HttpContext.Current;
            return ctx != null && ctx.Request != null ? ctx.Request.UserHostAddress ?? string.Empty : string.Empty;
        }

        [AllowAnonymous]
        [HttpPost]
        [Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequest request)
        {
            // Step 1: Validate the request.
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Username) ||
                string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(
                    "Username and password are required.");
            }

            string username = request.Username.Trim();

            // Correlation id ties the generic client-facing 500 to the detailed server-side log entry.
            string cid = Correlation.Current;   // request-scoped id set by RequestLoggingHandler
            string stage = "throttle";

            // One "login.stages" line per attempt: every stage reached (ok/FAIL), the outcome and the correlation id.
            var trace = new LoginTrace();
            int loginNo = System.Threading.Interlocked.Increment(ref _loginCount);
            string result = "exception";        // overwritten on every non-exception return
            int? knownUserId = null;            // internal id of an EXISTING account only (never the submitted name)

            try
            {
                string clientIp = GetClientIp();
                string throttleKey = LoginThrottle.Key(username, clientIp);
                if (LoginThrottle.IsLocked(throttleKey))
                {
                    result = "throttled";
                    return Ok(new LoginResponse
                    {
                        Success = false,
                        Message = "Too many failed attempts. Try again later."
                    });
                }

                trace.Ok("throttle");
                stage = "db-connect";
                using (var connection =
                    Db.CreateConnection())
                {
                    connection.Open();
                    trace.Ok("db-connect");

                    int userId;
                    string storedPassword;
                    string role;

                    // Step 2: Retrieve the user account and role.
                    stage = "user-lookup";
                    using (var command = new MySqlCommand(
                        @"SELECT id, password, type
                          FROM tbllogin
                          WHERE userName = @username
                          LIMIT 1",
                        connection))
                    {
                        command.Parameters.Add(
                            "@username",
                            MySqlDbType.VarChar,
                            255
                        ).Value = username;

                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read())
                            {
                                // Spend the same hashing time as a real check so response timing
                                // does not reveal whether the username exists.
                                trace.Split("user-lookup");   // timing only: the lookup itself ends here
                                PasswordHasher.VerifyPassword(request.Password, LoginThrottle.DummyHash);
                                LoginThrottle.RecordFailure(throttleKey);
                                trace.Ok("user-lookup", "dummy-hash");   // stages string unchanged; the hash time is reported as dummy-hash
                                result = "invalid_credentials";
                                return Ok(new LoginResponse
                                {
                                    Success = false,
                                    Message =
                                        "Invalid username or password."
                                });
                            }

                            userId = Convert.ToInt32(reader["id"]);
                            knownUserId = userId;

                            storedPassword =
                                reader["password"] == DBNull.Value
                                    ? null
                                    : Convert.ToString(
                                        reader["password"]);

                            role =
                                reader["type"] == DBNull.Value
                                    ? null
                                    : Convert.ToString(
                                        reader["type"]);
                        }
                    }

                    trace.Ok("user-lookup");

                    // Step 3: Verify the PBKDF2 password hash.
                    stage = "password-verify";
                    bool passwordValid =
                        PasswordHasher.VerifyPassword(
                            request.Password,
                            storedPassword
                        );

                    trace.Ok("password-verify");   // verification completed (valid or not)
                    if (!passwordValid)
                    {
                        result = "invalid_credentials";
                        LoginThrottle.RecordFailure(throttleKey);
                        return Ok(new LoginResponse
                        {
                            Success = false,
                            Message =
                                "Invalid username or password."
                        });
                    }

                    stage = "token-issue";
                    DateTime expiresUtc;
                    string token = TokenService.Issue(userId, username, role, out expiresUtc);
                    if (token == null)
                    {
                        result = "key_unavailable";
                        trace.Fail("token-issue");
                        // Fail closed: never authenticate without a configured signing key.
                        // Distinct event so a missing/short key is never confused with a lookup or DB exception.
                        // Structured field, NOT free text: free text is credential-scrubbed and
                        // "signingKey=..." would be masked as "signingKey=***", hiding the very value that matters.
                        DiagLog.Event("ERROR", "login.signing_key_unavailable", cid,
                            DiagLog.F("stage", stage, "outcome", "failed", "signingKey", AuthSettings.GetSigningKeyStatus()));
                        return ServerError(cid);
                    }

                    trace.Ok("token-issue");
                    LoginThrottle.Reset(throttleKey);

                    // Step 4: Update the last-login timestamp.
                    stage = "last-login-update";
                    using (var updateCommand = new MySqlCommand(
                        @"UPDATE tbllogin
                          SET lastLogin = @lastLogin
                          WHERE id = @userId",
                        connection))
                    {
                        updateCommand.Parameters.Add(
                            "@lastLogin",
                            MySqlDbType.DateTime
                        ).Value = DateTime.Now;

                        updateCommand.Parameters.Add(
                            "@userId",
                            MySqlDbType.Int32
                        ).Value = userId;

                        updateCommand.ExecuteNonQuery();
                    }
                    trace.Ok("last-login-update");

                    // Step 5: Return the authenticated user's details.
                    stage = "respond";
                    // "respond" = response object built. JSON serialisation happens after this method returns; a failure
                    // there is logged by DiagExceptionLogger / the http.request line (status 500) under the same id.
                    trace.Ok("respond");
                    result = "success";
                    return Ok(new LoginResponse
                    {
                        Success = true,
                        Message = "Login successful.",
                        UserId = userId,
                        Username = username,
                        Role = role,
                        Token = token,
                        TokenType = "Bearer",
                        ExpiresAtUtc = expiresUtc
                    });
                }
            }
            catch (Exception ex)
            {
                // Full detail goes to the protected server log only; the client gets a generic body + id.
                // Every logging call below swallows its own failures, so they can never replace this response.
                trace.Fail(stage);
                DiagLog.Error("Login", "login.exception", cid, ex, stage);
                return ServerError(cid);
            }
            finally
            {
                var f = DiagLog.F("stages", trace.ToString(), "outcome", result,
                                  "stageMs", trace.StageMs, "totalMs", trace.TotalMs,
                                  // Startup/deployment correlation: how long the process had been up, and which login this is
                                  // (loginNo 1 = the first login since the process started, i.e. the cold one).
                                  "sinceStartMs", (long)(DateTime.UtcNow - WebApiApplication.StartedUtc).TotalMilliseconds,
                                  "loginNo", loginNo,
                                  "build", WebApiApplication.BuildId,
                                  "pbkdf2", PasswordHasher.CurrentMode,
                                  // What the app itself sees, so RequireHttps can be switched on without guessing:
                                  "scheme", SafeScheme(), "xfp", SafeForwardedProto());
                if (knownUserId.HasValue) f["userId"] = knownUserId.Value;
                bool benign = result == "success" || result == "invalid_credentials" || result == "throttled";
                DiagLog.Event(benign ? "INFO" : "ERROR", "login.stages", cid, f);
            }
        }

        /// <summary>Compact record of the stages one login attempt went through, e.g. "throttle:ok/db-connect:ok/token-issue:FAIL".</summary>
        private sealed class LoginTrace
        {
            private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();
            private readonly System.Text.StringBuilder _ms = new System.Text.StringBuilder();
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
            private long _last;

            public void Ok(string stage, string msLabel = null) { Add(stage, stage + ":ok", msLabel ?? stage); }
            public void Fail(string stage) { Add(stage, stage + ":FAIL", stage); }

            /// <summary>Closes a timing segment WITHOUT adding a stage (keeps the existing "stages" string unchanged).</summary>
            public void Split(string msLabel) { AddMs(msLabel); }

            private void Add(string stage, string s, string msLabel)
            {
                if (_sb.Length > 0) _sb.Append('/');
                _sb.Append(s);
                AddMs(msLabel);
            }

            private void AddMs(string label)
            {
                // Duration of THIS segment (time since the previous one ended): "db-connect:3/password-verify:51".
                // Written as label:ms (never label=ms) so the log scrubber cannot mistake "password-verify=..." for a credential.
                long now = _clock.ElapsedMilliseconds;
                if (_ms.Length > 0) _ms.Append('/');
                _ms.Append(label).Append(':').Append(now - _last);
                _last = now;
            }

            public long TotalMs { get { return _clock.ElapsedMilliseconds; } }
            public string StageMs { get { return _ms.ToString(); } }
            public override string ToString() { return _sb.ToString(); }
        }

        private IHttpActionResult ServerError(string correlationId)
        {
            return Content(System.Net.HttpStatusCode.InternalServerError, new
            {
                Message = "Login could not be completed due to a server error.",
                CorrelationId = correlationId
            });
        }
    }
}
