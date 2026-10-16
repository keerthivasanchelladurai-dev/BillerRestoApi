using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BillerRestoApi.Security
{
    /// <summary>Issues and validates compact HS256 JWTs (header.payload.signature).</summary>
    public static class TokenService
    {
        private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

        public static string NormalizeRole(string role)
        {
            role = (role ?? string.Empty).Trim();
            return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : role;
        }

        /// <summary>Returns null if no signing key is configured.</summary>
        public static string Issue(int userId, string username, string role, out DateTime expiresUtc)
        {
            expiresUtc = DateTime.UtcNow.AddMinutes(AuthSettings.TokenLifetimeMinutes);
            byte[] key = AuthSettings.GetSigningKey();
            if (key == null) return null;

            var header = new JObject { { "alg", "HS256" }, { "typ", "JWT" } };
            var payload = new JObject
            {
                { "iss", AuthSettings.Issuer },
                { "aud", AuthSettings.Audience },
                { "sub", userId.ToString() },
                { "name", username },
                { "role", NormalizeRole(role) },
                { "iat", ToUnix(DateTime.UtcNow) },
                { "exp", ToUnix(expiresUtc) },
                { "jti", Guid.NewGuid().ToString("N") }
            };

            string signingInput = B64(header.ToString(Newtonsoft.Json.Formatting.None)) + "." +
                                  B64(payload.ToString(Newtonsoft.Json.Formatting.None));
            return signingInput + "." + Base64Url(Sign(signingInput, key));
        }

        /// <summary>Returns a principal for a valid, unexpired, correctly signed token; otherwise null.</summary>
        public static ClaimsPrincipal Validate(string token)
        {
            try
            {
                byte[] key = AuthSettings.GetSigningKey();
                if (key == null || string.IsNullOrEmpty(token) || token.Length > 4096) return null;

                string[] parts = token.Split('.');
                if (parts.Length != 3) return null;

                var header = JObject.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[0])));
                if ((string)header["alg"] != "HS256") return null; // rejects "none" and algorithm confusion

                byte[] expected = Sign(parts[0] + "." + parts[1], key);
                byte[] actual = FromBase64Url(parts[2]);
                if (!ConstantTimeEquals(expected, actual)) return null;

                var p = JObject.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[1])));
                if ((string)p["iss"] != AuthSettings.Issuer || (string)p["aud"] != AuthSettings.Audience) return null;

                long exp = (long)p["exp"];
                if (FromUnix(exp) + ClockSkew < DateTime.UtcNow) return null;

                int userId;
                string sub = (string)p["sub"];
                string name = (string)p["name"];
                string role = (string)p["role"];
                if (!int.TryParse(sub, out userId) || userId <= 0 || string.IsNullOrEmpty(name)) return null;

                var identity = new ClaimsIdentity(new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, sub),
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.Role, NormalizeRole(role))
                }, "Bearer", ClaimTypes.Name, ClaimTypes.Role);

                return new ClaimsPrincipal(identity);
            }
            catch
            {
                return null; // malformed token of any kind
            }
        }

        private static byte[] Sign(string input, byte[] key)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string B64(string s) { return Base64Url(Encoding.UTF8.GetBytes(s)); }

        private static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; case 1: throw new FormatException(); }
            return Convert.FromBase64String(s);
        }

        private static long ToUnix(DateTime utc) { return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }
        private static DateTime FromUnix(long s) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(s); }
    }
}
