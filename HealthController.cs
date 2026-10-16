using MySql.Data.MySqlClient;
using System;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using System.Web.Http;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        [AllowAnonymous]
        [HttpGet]
        [Route("database")]
        public IHttpActionResult CheckDatabase()
        {
            try
            {
                using (var connection =
                    Db.CreateConnection())
                {
                    connection.Open();
                    return Ok(new
                    {
                        status = "success",
                        message = "Database connection successful"
                    });
                }
            }
            catch (Exception ex)
            {
                Db.LogError("Health", ex);
                // 503 = the API is up but its configured database is unreachable (no details leaked).
                return Content(System.Net.HttpStatusCode.ServiceUnavailable, new
                {
                    status = "error",
                    message = "Service unavailable."
                });
            }
        }
    }
}