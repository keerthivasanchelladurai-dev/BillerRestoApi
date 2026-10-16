using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace BillerRestoApi.Security
{
    /// <summary>
    /// Registered globally, so every endpoint requires an authenticated user unless it carries
    /// [AllowAnonymous]. Use [ApiAuthorize(Roles = "Admin")] to additionally restrict by role.
    /// Responses: 401 when not authenticated, 403 when authenticated but not permitted; bodies are generic.
    /// </summary>
    public class ApiAuthorizeAttribute : AuthorizeAttribute
    {
        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            bool authenticated = actionContext.RequestContext.Principal != null &&
                                 actionContext.RequestContext.Principal.Identity != null &&
                                 actionContext.RequestContext.Principal.Identity.IsAuthenticated;

            var response = actionContext.Request.CreateResponse(
                authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                new { Message = authenticated ? "You do not have permission to perform this action." : "Authentication required." });

            if (!authenticated)
                response.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");

            actionContext.Response = response;
        }
    }

    /// <summary>Server-trusted identity of the caller (from the validated token, never from the request body).</summary>
    public static class CurrentUser
    {
        public static string Name(System.Security.Principal.IPrincipal principal)
        {
            string n = principal != null && principal.Identity != null ? principal.Identity.Name : null;
            if (string.IsNullOrEmpty(n)) return "unknown";
            return n.Length > 255 ? n.Substring(0, 255) : n;
        }
    }
}
