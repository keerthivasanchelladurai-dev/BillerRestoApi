using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using BillerRestoApi.Data;
using BillerRestoApi.Security;

namespace BillerRestoApi
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        /// <summary>When this worker process/AppDomain started (UTC). Lets log lines say how long after a start they happened.</summary>
        internal static readonly DateTime StartedUtc = DateTime.UtcNow;

        /// <summary>
        /// Short id of the deployed assembly build (module version id: changes with every compile). The assembly
        /// version stays 1.0.0.0, so this is the only way to tell from a log WHICH build is running.
        /// </summary>
        internal static string BuildId
        {
            get
            {
                try { return typeof(WebApiApplication).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 12); }
                catch { return "unknown"; }
            }
        }

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            LogStartup();
            WarmUpLoginPath();
        }

        // Background warm-up so the first login after a start/recycle does not pay for JIT, the one-time PBKDF2
        // self-check, the dummy hash and the first MySQL connection (driver load + pool creation).
        // Failures are ignored: the real request path still works exactly as before.
        private static void WarmUpLoginPath()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string db = "skipped";
                try { LoginThrottle.WarmUp(); } catch { }
                long hashMs = sw.ElapsedMilliseconds;
                try
                {
                    using (var c = Db.CreateConnection()) { c.Open(); db = "ok"; }
                }
                catch { db = "failed"; }

                DiagLog.Event("INFO", "app.warmup", null, DiagLog.F(
                    "build", BuildId, "pbkdf2", PasswordHasher.Mode, "hashWarmMs", hashMs, "db", db, "totalMs", sw.ElapsedMilliseconds));
            });
        }

        // One line per start: where the logs go and whether a usable signing key reached this process
        // (status only: never the key or its length). This is what diagnoses a login 500 caused by missing config.
        private static void LogStartup()
        {
            try
            {
                DiagLog.PurgeOld();   // also creates ~/App_Data/logs and proves it is writable by this identity

                string identity = null;
                try { identity = System.Security.Principal.WindowsIdentity.GetCurrent().Name; } catch { }
                var cs = System.Configuration.ConfigurationManager.ConnectionStrings[Db.ConnectionStringName];

                DiagLog.Event("INFO", "app.start", null, DiagLog.F(
                    "version", typeof(WebApiApplication).Assembly.GetName().Version.ToString(),
                    "identity", identity,                                   // the account that must have Modify on the log folder
                    "signingKey", AuthSettings.GetSigningKeyStatus(),       // status only; never the key or its length
                    "connectionStringPresent", cs != null && !string.IsNullOrWhiteSpace(cs.ConnectionString),
                    "build", BuildId,                                       // which compiled build is running (see BuildId)
                    "requireHttps", AuthSettings.RequireHttps,
                    "trustForwardedProto", AuthSettings.TrustForwardedProto,
                    "logDirSource", DiagLog.DirectorySource ?? "none",
                    "logDir", DiagLog.ResolvedDirectory,
                    "logDirWritable", DiagLog.DirectoryProblem == null));
            }
            catch { /* never block startup */ }
        }

        // One line when the application stops, with the reason ASP.NET reports (IdleTimeout, ConfigurationChange,
        // BinDirChangeOrDirectoryRename, ...). Together with app.start this shows whether a slow first login follows an
        // idle shutdown, a deployment (bin/Web.config change) or something outside ASP.NET (e.g. a pool memory limit,
        // which usually reports HostingEnvironment or None; the Windows System log then names the cause).
        protected void Application_End()
        {
            try
            {
                string reason = "unknown";
                try { reason = System.Web.Hosting.HostingEnvironment.ShutdownReason.ToString(); } catch { }
                DiagLog.Event("INFO", "app.end", null, DiagLog.F(
                    "build", BuildId, "reason", reason,
                    "uptimeSec", (long)(DateTime.UtcNow - StartedUtc).TotalSeconds));
            }
            catch { /* never throw while shutting down */ }
        }

        // Records exceptions that escape every controller (e.g. static-initializer or pipeline failures).
        // Does not clear the error, so the response is unchanged.
        protected void Application_Error()
        {
            try
            {
                Exception ex = Server.GetLastError();
                if (ex != null)
                    DiagLog.Error("Unhandled", "exception", DiagLog.NewCorrelationId(), ex.GetBaseException());
            }
            catch { /* never throw from the error handler */ }
        }
    }
}
