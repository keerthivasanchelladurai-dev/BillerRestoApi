using System;
using System.Configuration;
using System.Text;

namespace BillerRestoApi.Security
{
    /// <summary>
    /// Token / transport security settings. The signing key is never hard-coded: it comes from the
    /// BILLER_AUTH_SIGNING_KEY environment variable or the Auth:SigningKey app setting, which a
    /// deployment supplies through the (untracked) appSettings.secrets.config file referenced from Web.config.
    /// </summary>
    public static class AuthSettings
    {
        public const string Issuer = "BillerRestoApi";
        public const string Audience = "ThermalPrinterTest";
        private const int MinKeyLength = 32;

        public static int TokenLifetimeMinutes
        {
            get
            {
                int minutes;
                return int.TryParse(ConfigurationManager.AppSettings["Auth:TokenLifetimeMinutes"], out minutes)
                       && minutes >= 5 && minutes <= 24 * 60
                    ? minutes
                    : 12 * 60;
            }
        }

        public static bool RequireHttps
        {
            get
            {
                bool value;
                return bool.TryParse(ConfigurationManager.AppSettings["Security:RequireHttps"], out value) && value;
            }
        }

        /// <summary>True when HTTPS terminates at a reverse proxy that sets X-Forwarded-Proto.</summary>
        public static bool TrustForwardedProto
        {
            get
            {
                bool value;
                return bool.TryParse(ConfigurationManager.AppSettings["Security:TrustForwardedProto"], out value) && value;
            }
        }

        /// <summary>
        /// Diagnostic only, never reveals the key or its length. One of: "ok-env", "ok-config", "missing",
        /// "too-short-env", "too-short-config" (the suffix names the source that was selected).
        /// Mirrors the selection rules of <see cref="GetSigningKey"/>: a non-blank environment variable wins and is
        /// NOT supplemented by the app setting, so a short env var hides a good config key.
        /// </summary>
        public static string GetSigningKeyStatus()
        {
            string key = Environment.GetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY");
            string source = "env";
            if (string.IsNullOrWhiteSpace(key))
            {
                key = ConfigurationManager.AppSettings["Auth:SigningKey"];
                source = "config";
            }

            if (string.IsNullOrWhiteSpace(key)) return "missing";
            return (key.Trim().Length < MinKeyLength ? "too-short-" : "ok-") + source;
        }

        /// <summary>Returns the signing key bytes, or null when no adequate key is configured (fail closed).</summary>
        public static byte[] GetSigningKey()
        {
            string key = Environment.GetEnvironmentVariable("BILLER_AUTH_SIGNING_KEY");
            if (string.IsNullOrWhiteSpace(key))
                key = ConfigurationManager.AppSettings["Auth:SigningKey"];

            if (string.IsNullOrWhiteSpace(key) || key.Trim().Length < MinKeyLength)
                return null;

            return Encoding.UTF8.GetBytes(key.Trim());
        }
    }
}
