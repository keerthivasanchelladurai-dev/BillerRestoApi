using MySql.Data.MySqlClient;
using System;
using System.Configuration;

namespace BillerRestoApi.Data
{
    /// <summary>
    /// Single entry point for database access. Every connection in the API is created here from the
    /// one named connection string in Web.config. To point the API at another database change that
    /// entry and recycle the app pool - no source change or recompile is needed.
    /// </summary>
    public static class Db
    {
        public const string ConnectionStringName = "BillerRestoDb";

        // Test seam: internal, visible only to the unit-test assembly (InternalsVisibleTo). Lets tests target an
        // isolated throw-away schema. Always null in production; nothing outside this assembly can set it.
        internal static string ConnectionStringOverrideForTests;

        public static MySqlConnection CreateConnection()
        {
            if (ConnectionStringOverrideForTests != null)
                return new MySqlConnection(ConnectionStringOverrideForTests);

            var settings = ConfigurationManager.ConnectionStrings[ConnectionStringName];
            if (settings == null || string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                throw new ConfigurationErrorsException(
                    "Connection string '" + ConnectionStringName + "' is missing or empty in Web.config.");
            }

            return new MySqlConnection(settings.ConnectionString);
        }

        /// <summary>
        /// Logs a server-side failure to the structured diagnostic log (see <see cref="DiagLog"/>).
        /// Never includes the connection string; credential-looking text is scrubbed. Never throws.
        /// </summary>
        public static void LogError(string area, Exception ex)
        {
            DiagLog.Error(area, "exception", Correlation.Current, ex);   // same id the client receives
        }
    }
}
