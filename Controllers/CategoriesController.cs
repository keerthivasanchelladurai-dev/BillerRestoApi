using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using System.Web.Http;
using BillerRestoApi.Models;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/categories")]
    public class CategoriesController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetCategories()
        {
            try
            {
                var categories = new List<CategoryDto>();
                const string query = "SELECT id, name, show_order FROM master_category WHERE is_delete = @is_delete ORDER BY show_order ASC, id ASC;";

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
                            int showOrderOrdinal = reader.GetOrdinal("show_order");

                            while (reader.Read())
                            {
                                categories.Add(new CategoryDto
                                {
                                    Id = reader.IsDBNull(idOrdinal) ? 0 : Convert.ToInt32(reader[idOrdinal]),
                                    Name = reader.IsDBNull(nameOrdinal) ? string.Empty : reader[nameOrdinal].ToString(),
                                    ShowOrder = reader.IsDBNull(showOrderOrdinal) ? 0 : Convert.ToInt32(reader[showOrderOrdinal])
                                });
                            }
                        }
                    }
                }

                return Ok(categories);
            }
            catch (Exception ex)
            {
                Db.LogError("Categories", ex);
                // Return generic 500 without exposing connection strings, credentials, or internal exception details
                return InternalServerError();
            }
        }

        [ApiAuthorize(Roles = "Admin")]
        [HttpPost]
        [Route("")]
        public IHttpActionResult CreateCategory([FromBody] CreateCategoryRequestDto request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Category name is required.");
            }

            string trimmedName = request.Name.Trim();
            if (trimmedName.Length > 255)
            {
                return BadRequest("Category name cannot exceed 255 characters.");
            }

            int showOrder = request.ShowOrder.GetValueOrDefault(1000);
            string createdBy = CurrentUser.Name(User); // trusted identity from the token, not the request body

            try
            {
                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    // Check for duplicate active category name (case-insensitive)
                    using (var checkCmd = connection.CreateCommand())
                    {
                        checkCmd.CommandText = "SELECT id FROM master_category WHERE LOWER(TRIM(name)) = LOWER(@name) AND is_delete = 0 LIMIT 1;";
                        checkCmd.Parameters.Add("@name", MySqlDbType.VarChar).Value = trimmedName;
                        object existing = checkCmd.ExecuteScalar();
                        if (existing != null)
                        {
                            return BadRequest($"An active category with the name '{trimmedName}' already exists.");
                        }
                    }

                    // Insert category
                    long newId;
                    using (var insertCmd = connection.CreateCommand())
                    {
                        insertCmd.CommandText = @"
                            INSERT INTO master_category (
                                name, is_delete, created_date, created_by, show_order
                            ) VALUES (
                                @name, 0, @createdDate, @createdBy, @showOrder
                            );";

                        insertCmd.Parameters.Add("@name", MySqlDbType.VarChar).Value = trimmedName;
                        insertCmd.Parameters.Add("@createdDate", MySqlDbType.Date).Value = DateTime.Today;
                        insertCmd.Parameters.Add("@createdBy", MySqlDbType.VarChar).Value = createdBy;
                        insertCmd.Parameters.Add("@showOrder", MySqlDbType.Int32).Value = showOrder;

                        insertCmd.ExecuteNonQuery();
                        newId = insertCmd.LastInsertedId;
                    }

                    var createdCategory = new CategoryDto
                    {
                        Id = (int)newId,
                        Name = trimmedName,
                        ShowOrder = showOrder
                    };

                    return Content(System.Net.HttpStatusCode.Created, createdCategory);
                }
            }
            catch (Exception ex)
            {
                Db.LogError("Categories", ex);
                return InternalServerError();
            }
        }
    }
}
