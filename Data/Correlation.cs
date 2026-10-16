using System;
using System.Net.Http;
using System.Runtime.Remoting.Messaging;
using System.Web;

namespace BillerRestoApi.Data
{
    /// <summary>
    /// Request-scoped correlation id. Flows with the logical call context (survives await) so controllers,
    /// filters and the logger agree on one id per request. Incoming ids are accepted only if they match
    /// [A-Za-z0-9-]{8,64}; anything else is replaced by a fresh server-generated id.
    /// </summary>
    public static class Correlation
    {
        public const string HeaderName = "X-Correlation-Id";
        private const string SlotName = "biller.cid";

        public static string Current
        {
            get
            {
                string id = CallContext.LogicalGetData(SlotName) as string;
                if (id == null)
                {
                    try
                    {
                        var ctx = HttpContext.Current;
                        if (ctx != null) id = ctx.Items[SlotName] as string;
                    }
                    catch { }
                }
                if (id == null)
                {
                    id = DiagLog.NewCorrelationId();
                    Set(id);
                }
                return id;
            }
        }

        public static void Set(string id)
        {
            CallContext.LogicalSetData(SlotName, id);
            try
            {
                var ctx = HttpContext.Current;
                if (ctx != null) ctx.Items[SlotName] = id;
            }
            catch { }
        }

        /// <summary>Adopts a valid client-supplied id, otherwise generates one. Never trusts the raw header.</summary>
        public static string FromRequest(HttpRequestMessage request)
        {
            try
            {
                System.Collections.Generic.IEnumerable<string> values;
                if (request != null && request.Headers.TryGetValues(HeaderName, out values))
                {
                    foreach (string v in values)
                        if (DiagLog.IsValidCorrelationId(v)) return v;
                }
            }
            catch { }
            return DiagLog.NewCorrelationId();
        }
    }
}
