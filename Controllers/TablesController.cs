using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using BillerRestoApi.Data;
using System.Web.Http;
using BillerRestoApi.Models;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/tables")]
    public class TablesController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetTables([FromUri] int? locationId = null)
        {
            try
            {
                var tables = new List<TableDto>();
                string query = "SELECT id, name, location_id FROM master_table WHERE is_delete = @is_delete";
                if (locationId.HasValue)
                {
                    query += " AND location_id = @location_id";
                }
                query += " ORDER BY id ASC;";

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.Add("@is_delete", MySqlDbType.Int32).Value = 0;
                        if (locationId.HasValue)
                        {
                            command.Parameters.Add("@location_id", MySqlDbType.Int32).Value = locationId.Value;
                        }

                        using (var reader = command.ExecuteReader())
                        {
                            int idOrdinal = reader.GetOrdinal("id");
                            int nameOrdinal = reader.GetOrdinal("name");
                            int locationIdOrdinal = reader.GetOrdinal("location_id");

                            while (reader.Read())
                            {
                                tables.Add(new TableDto
                                {
                                    Id = reader.IsDBNull(idOrdinal) ? 0 : Convert.ToInt32(reader[idOrdinal]),
                                    Name = reader.IsDBNull(nameOrdinal) ? string.Empty : reader[nameOrdinal].ToString(),
                                    LocationId = reader.IsDBNull(locationIdOrdinal) ? (int?)null : Convert.ToInt32(reader[locationIdOrdinal])
                                });
                            }
                        }
                    }
                }

                return Ok(tables);
            }
            catch (Exception ex)
            {
                Db.LogError("Tables", ex);
                // Return generic 500 without exposing connection strings, credentials, or internal exception details
                return InternalServerError();
            }
        }
    }
}
