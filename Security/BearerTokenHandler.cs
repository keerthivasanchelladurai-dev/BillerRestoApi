using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace BillerRestoApi.Security
{
    /// <summary>
    /// Runs for every Web API request: enforces HTTPS when configured and turns a valid
    /// "Authorization: Bearer" token into the request principal. An invalid token simply leaves the
    /// request anonymous; the global authorization filter then rejects it unless the action is [AllowAnonymous].
    /// </summary>
    public class BearerTokenHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (AuthSettings.RequireHttps && !IsHttps(request))
            {
                // Deliberately NOT a redirect: that would resend credentials/bodies over plain HTTP.
                return request.CreateResponse(HttpStatusCode.Forbidden, new { Message = "HTTPS is required." });
            }

            ClaimsPrincipal principal = null;
            AuthenticationHeaderValue auth = request.Headers.Authorization;
            if (auth != null && string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                principal = TokenService.Validate(auth.Parameter);

            if (principal != null)
            {
                request.GetRequestContext().Principal = principal;
                Thread.CurrentPrincipal = principal;
                if (HttpContext.Current != null) HttpContext.Current.User = principal;
            }

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            if (AuthSettings.RequireHttps)
                response.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=31536000");
            response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
            return response;
        }

        private static bool IsHttps(HttpRequestMessage request)
        {
            if (request.RequestUri != null && request.RequestUri.Scheme == "https") return true;
            if (AuthSettings.TrustForwardedProto)
            {
                IEnumerable<string> values;
                if (request.Headers.TryGetValues("X-Forwarded-Proto", out values))
                    foreach (var v in values)
                        if (string.Equals(v, "https", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
