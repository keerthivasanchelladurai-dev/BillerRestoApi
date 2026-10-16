using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using BillerRestoApi.Data;
using System.Web.Http;
using BillerRestoApi.Models;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/locations")]
    public class LocationsController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetLocations()
        {
            try
            {
                var locations = new List<LocationDto>();
                const string query = "SELECT id, name FROM master_location WHERE is_delete = @is_delete ORDER BY id ASC;";

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.Add("@is_delete", MySqlDbType.Int32).Value = 0;

                        using (var reader = command.ExecuteReader())
                        {
                            int idOrdinal = reader.GetOrdinal("id");
                            int nameOrdinal = reader.GetOrdinal("name");

                            while (reader.Read())
                            {
                                locations.Add(new LocationDto
                                {
                                    Id = reader.IsDBNull(idOrdinal) ? 0 : Convert.ToInt32(reader[idOrdinal]),
                                    Name = reader.IsDBNull(nameOrdinal) ? string.Empty : reader[nameOrdinal].ToString()
                                });
                            }
                        }
                    }
                }

                return Ok(locations);
            }
            catch (Exception ex)
            {
                Db.LogError("Locations", ex);
                // Return generic 500 without exposing connection strings, credentials, or internal exception details
                return InternalServerError();
            }
        }
    }
}
