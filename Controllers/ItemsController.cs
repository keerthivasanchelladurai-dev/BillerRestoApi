using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using BillerRestoApi.Models;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Web;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/items")]
    public class ItemsController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetItems(
            [FromUri] int? locationId = null,
            [FromUri] int? categoryId = null,
            [FromUri] string search = null,
            [FromUri] int? page = null,
            [FromUri] int? pageSize = null,
            [FromUri] bool all = false)
        {
            try
            {
                // By default, pagination is ALWAYS enforced (page 1, pageSize 10) to prevent unbounded retrieval.
                // 'all=true' can be passed only when an unpaginated administrative list is explicitly requested.
                bool isPaginated = !all;
                int effectivePage = Math.Max(1, page.GetValueOrDefault(1));
                int effectivePageSize = Math.Max(1, Math.Min(100, pageSize.GetValueOrDefault(10)));
                int offset = (effectivePage - 1) * effectivePageSize;

                var whereClause = new StringBuilder(" WHERE i.isDelete = @isDelete");
                if (locationId.HasValue)
                {
                    whereClause.Append(" AND p.location_id = @locationId");
                }

                if (categoryId.HasValue && categoryId.Value > 0)
                {
                    whereClause.Append(" AND i.category_id = @categoryId");
                }

                bool hasSearch = !string.IsNullOrWhiteSpace(search);
                if (hasSearch)
                {
                    whereClause.Append(" AND (i.name LIKE @searchPattern OR CAST(i.user_code AS CHAR) = @searchExact)");
                }

                var items = new List<ItemDto>();
                int totalCount = 0;

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    if (isPaginated)
                    {
                        // Get total matching count
                        string countSql = $@"
                            SELECT COUNT(DISTINCT i.id)
                            FROM tblitems i
                            INNER JOIN tblitemsprice p ON i.id = p.item_id
                            {whereClause};";

                        using (var countCmd = new MySqlCommand(countSql, connection))
                        {
                            countCmd.Parameters.Add("@isDelete", MySqlDbType.Int32).Value = 0;
                            if (locationId.HasValue) countCmd.Parameters.Add("@locationId", MySqlDbType.Int32).Value = locationId.Value;
                            if (categoryId.HasValue && categoryId.Value > 0) countCmd.Parameters.Add("@categoryId", MySqlDbType.Int32).Value = categoryId.Value;
                            if (hasSearch)
                            {
                                countCmd.Parameters.Add("@searchPattern", MySqlDbType.VarChar).Value = "%" + search.Trim() + "%";
                                countCmd.Parameters.Add("@searchExact", MySqlDbType.VarChar).Value = search.Trim();
                            }

                            object countObj = countCmd.ExecuteScalar();
                            totalCount = countObj != null && countObj != DBNull.Value ? Convert.ToInt32(countObj) : 0;
                        }
                    }

                    // Query items
                    var queryBuilder = new StringBuilder($@"
                        SELECT 
                            i.id,
                            i.name,
                            i.category_id,
                            p.location_id,
                            p.price,
                            i.user_code,
                            i.units,
                            i.is_kot,
                            i.show_order,
                            i.image_path
                        FROM tblitems i
                        INNER JOIN tblitemsprice p ON i.id = p.item_id
                        {whereClause}");

                    if (isPaginated)
                    {
                        // Requirement 2: Reliable ordering field for most recent items is i.id DESC
                        queryBuilder.Append(" ORDER BY i.id DESC, p.location_id ASC LIMIT @limit OFFSET @offset;");
                    }
                    else
                    {
                        // Legacy default ordering when unpaginated
                        queryBuilder.Append(" ORDER BY i.show_order ASC, i.id ASC, p.location_id ASC;");
                    }

                    using (var command = new MySqlCommand(queryBuilder.ToString(), connection))
                    {
                        command.Parameters.Add("@isDelete", MySqlDbType.Int32).Value = 0;

                        if (locationId.HasValue)
                        {
                            command.Parameters.Add("@locationId", MySqlDbType.Int32).Value = locationId.Value;
                        }

                        if (categoryId.HasValue && categoryId.Value > 0)
                        {
                            command.Parameters.Add("@categoryId", MySqlDbType.Int32).Value = categoryId.Value;
                        }

                        if (hasSearch)
                        {
                            command.Parameters.Add("@searchPattern", MySqlDbType.VarChar).Value = "%" + search.Trim() + "%";
                            command.Parameters.Add("@searchExact", MySqlDbType.VarChar).Value = search.Trim();
                        }

                        if (isPaginated)
                        {
                            command.Parameters.Add("@limit", MySqlDbType.Int32).Value = effectivePageSize;
                            command.Parameters.Add("@offset", MySqlDbType.Int32).Value = offset;
                        }

                        using (var reader = command.ExecuteReader())
                        {
                            int idOrdinal = reader.GetOrdinal("id");
                            int nameOrdinal = reader.GetOrdinal("name");
                            int categoryIdOrdinal = reader.GetOrdinal("category_id");
                            int locationIdOrdinal = reader.GetOrdinal("location_id");
                            int priceOrdinal = reader.GetOrdinal("price");
                            int userCodeOrdinal = reader.GetOrdinal("user_code");
                            int unitsOrdinal = reader.GetOrdinal("units");
                            int isKotOrdinal = reader.GetOrdinal("is_kot");
                            int showOrderOrdinal = reader.GetOrdinal("show_order");
                            int imagePathOrdinal = reader.GetOrdinal("image_path");

                            while (reader.Read())
                            {
                                items.Add(new ItemDto
                                {
                                    Id = reader.IsDBNull(idOrdinal) ? 0 : Convert.ToInt32(reader[idOrdinal]),
                                    Name = reader.IsDBNull(nameOrdinal) ? string.Empty : reader[nameOrdinal].ToString(),
                                    CategoryId = reader.IsDBNull(categoryIdOrdinal) ? (int?)null : Convert.ToInt32(reader[categoryIdOrdinal]),
                                    LocationId = reader.IsDBNull(locationIdOrdinal) ? 0 : Convert.ToInt32(reader[locationIdOrdinal]),
                                    Price = reader.IsDBNull(priceOrdinal) ? 0m : Convert.ToDecimal(reader[priceOrdinal]),
                                    UserCode = reader.IsDBNull(userCodeOrdinal) ? (int?)null : Convert.ToInt32(reader[userCodeOrdinal]),
                                    Units = reader.IsDBNull(unitsOrdinal) ? string.Empty : reader[unitsOrdinal].ToString(),
                                    IsKot = reader.IsDBNull(isKotOrdinal) ? 0 : Convert.ToInt32(reader[isKotOrdinal]),
                                    ShowOrder = reader.IsDBNull(showOrderOrdinal) ? 0 : Convert.ToInt32(reader[showOrderOrdinal]),
                                    ImagePath = reader.IsDBNull(imagePathOrdinal) ? null : reader[imagePathOrdinal].ToString()
                                });
                            }
                        }
                    }
                }

                if (isPaginated && Request != null)
                {
                    bool hasMore = offset + items.Count < totalCount;
                    var response = Request.CreateResponse(System.Net.HttpStatusCode.OK, items);
                    response.Headers.Add("X-Total-Count", totalCount.ToString());
                    response.Headers.Add("X-Page", effectivePage.ToString());
                    response.Headers.Add("X-Page-Size", effectivePageSize.ToString());
                    response.Headers.Add("X-Has-More", hasMore ? "true" : "false");
                    return ResponseMessage(response);
                }

                return Ok(items);
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                // Return generic 500 without exposing connection strings, credentials, or internal exception details
                return InternalServerError();
            }
        }


// ------------------------------------------------------------------
        // POST api/items/{id}/image  -> upload (or replace) an item photo
        // ------------------------------------------------------------------
        [ApiAuthorize(Roles = "Admin")]
        [HttpPost]
        [Route("{id:int}/image")]
        public async System.Threading.Tasks.Task<IHttpActionResult> UploadItemImage(int id)
        {
            if (id <= 0)
                return BadRequest("Invalid item ID.");

            if (!Request.Content.IsMimeMultipartContent())
            {
                return BadRequest("Multipart image upload is required.");
            }

            string imageDirectory = HttpContext.Current.Server.MapPath("~/UploadedImages/");

            try
            {
                Directory.CreateDirectory(imageDirectory);

                var provider = new MultipartMemoryStreamProvider();
                await Request.Content.ReadAsMultipartAsync(provider);

                var file = provider.Contents.FirstOrDefault();

                if (file == null)
                {
                    return BadRequest("Please select an image.");
                }

                if (file.Headers.ContentLength.HasValue &&
                    file.Headers.ContentLength.Value > 5 * 1024 * 1024)
                {
                    return BadRequest("Image size cannot exceed 5 MB.");
                }

                string mediaType = file.Headers.ContentType?.MediaType?.ToLowerInvariant();

                string extension;

                switch (mediaType)
                {
                    case "image/jpeg":
                        extension = ".jpg";
                        break;
                    case "image/png":
                        extension = ".png";
                        break;
                    case "image/webp":
                        extension = ".webp";
                        break;
                    default:
                        return BadRequest("Only JPEG, PNG, and WebP images are allowed.");
                }

                byte[] bytes = await file.ReadAsByteArrayAsync();

                if (bytes.Length == 0 || bytes.Length > 5 * 1024 * 1024)
                {
                    return BadRequest("Invalid image size.");
                }

                // Validate actual image file signature.
                bool validImage = false;

                if (extension == ".jpg" && bytes.Length >= 3)
                    validImage = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

                if (extension == ".png" && bytes.Length >= 8)
                    validImage = bytes[0] == 0x89 && bytes[1] == 0x50 &&
                                 bytes[2] == 0x4E && bytes[3] == 0x47 &&
                                 bytes[4] == 0x0D && bytes[5] == 0x0A &&
                                 bytes[6] == 0x1A && bytes[7] == 0x0A;

                if (extension == ".webp" && bytes.Length >= 12)
                    validImage = bytes[0] == 0x52 && bytes[1] == 0x49 &&
                                 bytes[2] == 0x46 && bytes[3] == 0x46 &&
                                 bytes[8] == 0x57 && bytes[9] == 0x45 &&
                                 bytes[10] == 0x42 && bytes[11] == 0x50;

                if (!validImage)
                {
                    return BadRequest("The uploaded file is not a valid image.");
                }

                string imageName = Guid.NewGuid().ToString("N") + extension;
                string fullPath = Path.Combine(imageDirectory, imageName);
                string relativePath = "/UploadedImages/" + imageName;

                // Write the file first; if the DB step fails we delete it again (no dangling DB paths).
                File.WriteAllBytes(fullPath, bytes);

                string oldImagePath = null;
                bool saved = false;

                try
                {
                    using (var connection = Db.CreateConnection())
                    {
                        await connection.OpenAsync();

                        using (var selectCmd = new MySqlCommand(
                            "SELECT image_path FROM tblitems WHERE id = @id AND isDelete = 0 LIMIT 1;", connection))
                        {
                            selectCmd.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
                            object existing = await selectCmd.ExecuteScalarAsync();

                            if (existing == null)
                                return NotFound();   // item does not exist (finally block removes the new file)

                            oldImagePath = existing == DBNull.Value ? null : existing.ToString();
                        }

                        using (var command = new MySqlCommand(
                            "UPDATE tblitems SET image_path = @imagePath, updatedDate = @updatedDate " +
                            "WHERE id = @id AND isDelete = 0;", connection))
                        {
                            command.Parameters.Add("@imagePath", MySqlDbType.VarChar).Value = relativePath;
                            command.Parameters.Add("@updatedDate", MySqlDbType.DateTime).Value = DateTime.Now;
                            command.Parameters.Add("@id", MySqlDbType.Int32).Value = id;

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    saved = true;
                }
                finally
                {
                    if (!saved)
                        TryDeleteImageFile(imageDirectory, relativePath);
                }

                // The old photo is no longer referenced - remove it so the folder does not grow forever.
                TryDeleteImageFile(imageDirectory, oldImagePath);

                return Ok(new
                {
                    Success = true,
                    ItemId = id,
                    ImagePath = relativePath
                });
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                return InternalServerError();
            }
        }

        // ------------------------------------------------------------------
        // DELETE api/items/{id}/image  -> remove the item photo
        // ------------------------------------------------------------------
        [ApiAuthorize(Roles = "Admin")]
        [HttpDelete]
        [Route("{id:int}/image")]
        public IHttpActionResult DeleteItemImage(int id)
        {
            if (id <= 0)
                return BadRequest("Invalid item ID.");

            string imageDirectory = HttpContext.Current.Server.MapPath("~/UploadedImages/");

            try
            {
                string oldImagePath;

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    using (var selectCmd = new MySqlCommand(
                        "SELECT image_path FROM tblitems WHERE id = @id AND isDelete = 0 LIMIT 1;", connection))
                    {
                        selectCmd.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
                        object existing = selectCmd.ExecuteScalar();

                        if (existing == null)
                            return NotFound();

                        oldImagePath = existing == DBNull.Value ? null : existing.ToString();
                    }

                    using (var command = new MySqlCommand(
                        "UPDATE tblitems SET image_path = NULL, updatedDate = @updatedDate " +
                        "WHERE id = @id AND isDelete = 0;", connection))
                    {
                        command.Parameters.Add("@updatedDate", MySqlDbType.DateTime).Value = DateTime.Now;
                        command.Parameters.Add("@id", MySqlDbType.Int32).Value = id;
                        command.ExecuteNonQuery();
                    }
                }

                TryDeleteImageFile(imageDirectory, oldImagePath);

                return Ok(new { Success = true, ItemId = id });
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                return InternalServerError();
            }
        }

        // ------------------------------------------------------------------
        // GET api/items/{id}  -> everything needed to pre-fill the Edit form
        // ------------------------------------------------------------------
        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult GetItemById(int id)
        {
            if (id <= 0)
                return BadRequest("Invalid item ID.");

            try
            {
                ItemDetailDto item = null;

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    using (var command = new MySqlCommand(@"
                        SELECT id, name, category_id, units, tamil_name,
                               user_code, is_kot, show_order, image_path
                        FROM tblitems
                        WHERE id = @id AND isDelete = 0
                        LIMIT 1;", connection))
                    {
                        command.Parameters.Add("@id", MySqlDbType.Int32).Value = id;

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                item = new ItemDetailDto
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    Name = reader["name"] == DBNull.Value ? string.Empty : reader["name"].ToString(),
                                    CategoryId = reader["category_id"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["category_id"]),
                                    Units = reader["units"] == DBNull.Value ? string.Empty : reader["units"].ToString(),
                                    TamilName = reader["tamil_name"] == DBNull.Value ? string.Empty : reader["tamil_name"].ToString(),
                                    UserCode = reader["user_code"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["user_code"]),
                                    IsKot = reader["is_kot"] == DBNull.Value ? 0 : Convert.ToInt32(reader["is_kot"]),
                                    ShowOrder = reader["show_order"] == DBNull.Value ? 0 : Convert.ToInt32(reader["show_order"]),
                                    ImagePath = reader["image_path"] == DBNull.Value ? null : reader["image_path"].ToString()
                                };
                            }
                        }
                    }

                    if (item == null)
                        return NotFound();

                    using (var priceCmd = new MySqlCommand(
                        "SELECT location_id, price FROM tblitemsprice WHERE item_id = @id ORDER BY location_id;", connection))
                    {
                        priceCmd.Parameters.Add("@id", MySqlDbType.Int32).Value = id;

                        using (var reader = priceCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                item.Prices.Add(new ItemDetailPriceDto
                                {
                                    LocationId = Convert.ToInt32(reader["location_id"]),
                                    Price = reader["price"] == DBNull.Value ? 0m : Convert.ToDecimal(reader["price"])
                                });
                            }
                        }
                    }
                }

                return Ok(item);
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                return InternalServerError();
            }
        }

        // ------------------------------------------------------------------
        // PUT api/items/{id}  -> update item details (and, optionally, prices / user code)
        // ------------------------------------------------------------------
        [ApiAuthorize(Roles = "Admin")]
        [HttpPut]
        [Route("{id:int}")]
        public IHttpActionResult UpdateItem(int id, [FromBody] UpdateItemDetailsRequestDto request)
        {
            if (id <= 0)
                return BadRequest("Invalid item ID.");

            if (request == null)
                return BadRequest("Request body cannot be empty.");

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Item name is required.");

            if (request.Name.Trim().Length > 255)
                return BadRequest("Item name cannot exceed 255 characters.");

            if (request.CategoryId <= 0)
                return BadRequest("A valid category ID is required.");

            string units = string.IsNullOrWhiteSpace(request.Units)
                ? "Nos"
                : request.Units.Trim();

            if (units.Length > 255)
                return BadRequest("Units cannot exceed 255 characters.");

            if (request.UserCode.HasValue && request.UserCode.Value <= 0)
                return BadRequest("user_code must be greater than zero if provided.");

            // Optional location prices (1 = TABLE, 2 = PARCEL, 8 = ONLINE)
            var resolvedPrices = new Dictionary<int, decimal>();

            if (request.Prices != null)
            {
                foreach (var p in request.Prices)
                {
                    if (p.LocationId != 1 && p.LocationId != 2 && p.LocationId != 8)
                        return BadRequest("Invalid location_id '" + p.LocationId + "'. Supported locations are 1 (TABLE), 2 (PARCEL), and 8 (ONLINE).");

                    if (p.Price < 0m)
                        return BadRequest("Price for location " + p.LocationId + " cannot be negative.");

                    resolvedPrices[p.LocationId] = Math.Round(p.Price, 2);
                }
            }

            try
            {
                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    // Same rule as CreateItem: the category must exist and be active.
                    using (var catCmd = new MySqlCommand(
                        "SELECT id FROM master_category WHERE id = @catId AND is_delete = 0 LIMIT 1;", connection))
                    {
                        catCmd.Parameters.Add("@catId", MySqlDbType.Int32).Value = request.CategoryId;
                        if (catCmd.ExecuteScalar() == null)
                            return BadRequest("Category ID " + request.CategoryId + " does not exist or is inactive.");
                    }

                    using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            using (var command = new MySqlCommand(@"
                                UPDATE tblitems
                                SET name = @name,
                                    category_id = @categoryId,
                                    units = @units,
                                    tamil_name = @tamilName,
                                    is_kot = @isKot,
                                    show_order = @showOrder,
                                    user_code = COALESCE(@userCode, user_code),
                                    price = COALESCE(@basePrice, price),
                                    updatedDate = @updatedDate
                                WHERE id = @id AND isDelete = 0;", connection, transaction))
                            {
                                command.Parameters.Add("@name", MySqlDbType.VarChar).Value = request.Name.Trim();
                                command.Parameters.Add("@categoryId", MySqlDbType.Int32).Value = request.CategoryId;
                                command.Parameters.Add("@units", MySqlDbType.VarChar).Value = units;
                                command.Parameters.Add("@tamilName", MySqlDbType.VarChar).Value =
                                    (object)(request.TamilName == null ? string.Empty : request.TamilName.Trim());
                                command.Parameters.Add("@isKot", MySqlDbType.Int32).Value = request.IsKot;
                                command.Parameters.Add("@showOrder", MySqlDbType.Int32).Value = request.ShowOrder;
                                command.Parameters.Add("@userCode", MySqlDbType.Int32).Value =
                                    request.UserCode.HasValue ? (object)request.UserCode.Value : DBNull.Value;
                                command.Parameters.Add("@basePrice", MySqlDbType.Double).Value =
                                    resolvedPrices.ContainsKey(1) ? (object)(double)resolvedPrices[1] : DBNull.Value;
                                command.Parameters.Add("@updatedDate", MySqlDbType.DateTime).Value = DateTime.Now;
                                command.Parameters.Add("@id", MySqlDbType.Int32).Value = id;

                                if (command.ExecuteNonQuery() == 0)
                                {
                                    transaction.Rollback();
                                    return NotFound();
                                }
                            }

                            // Upsert each supplied location price.
                            foreach (var kvp in resolvedPrices)
                            {
                                bool exists;

                                using (var existsCmd = new MySqlCommand(
                                    "SELECT COUNT(*) FROM tblitemsprice WHERE item_id = @itemId AND location_id = @locId;",
                                    connection, transaction))
                                {
                                    existsCmd.Parameters.Add("@itemId", MySqlDbType.Int32).Value = id;
                                    existsCmd.Parameters.Add("@locId", MySqlDbType.Int32).Value = kvp.Key;
                                    exists = Convert.ToInt32(existsCmd.ExecuteScalar()) > 0;
                                }

                                string sql = exists
                                    ? "UPDATE tblitemsprice SET price = @price WHERE item_id = @itemId AND location_id = @locId;"
                                    : "INSERT INTO tblitemsprice (location_id, item_id, price) VALUES (@locId, @itemId, @price);";

                                using (var priceCmd = new MySqlCommand(sql, connection, transaction))
                                {
                                    priceCmd.Parameters.Add("@locId", MySqlDbType.Int32).Value = kvp.Key;
                                    priceCmd.Parameters.Add("@itemId", MySqlDbType.Int32).Value = id;
                                    priceCmd.Parameters.Add("@price", MySqlDbType.Double).Value = (double)kvp.Value;
                                    priceCmd.ExecuteNonQuery();
                                }
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }

                return Ok(new { Success = true, ItemId = id });
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                return InternalServerError();
            }
        }

        // Deletes a previously uploaded image file. Only the file NAME is used, always inside the
        // UploadedImages folder, so a tampered database value can never delete anything elsewhere.
        private static void TryDeleteImageFile(string imageDirectory, string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(imageDirectory))
                    return;

                string fileName = Path.GetFileName(relativePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    return;

                string fullPath = Path.Combine(imageDirectory, fileName);
                if (File.Exists(fullPath))
                    File.Delete(fullPath);
            }
            catch
            {
                // Best effort only - a leftover file is harmless.
            }
        }

        [ApiAuthorize(Roles = "Admin")]
        [HttpPost]
        [Route("")]
        public IHttpActionResult CreateItem([FromBody] CreateItemRequestDto request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Item name is required.");
            }

            string trimmedName = request.Name.Trim();
            if (trimmedName.Length > 255)
            {
                return BadRequest("Item name cannot exceed 255 characters.");
            }

            if (request.CategoryId <= 0)
            {
                return BadRequest("A valid category_id is required.");
            }

            string units = string.IsNullOrWhiteSpace(request.Units) ? "Nos" : request.Units.Trim();
            if (units.Length > 255)
            {
                units = units.Substring(0, 255);
            }

            string tamilName = request.TamilName?.Trim() ?? string.Empty;
            if (tamilName.Length > 255)
            {
                tamilName = tamilName.Substring(0, 255);
            }

            int isKot = request.IsKot.GetValueOrDefault(1);
            int showOrder = request.ShowOrder.GetValueOrDefault(0);

            // Resolve and validate location prices:
            // Supported locations: 1 (TABLE), 2 (PARCEL), 8 (ONLINE)
            var resolvedPrices = new Dictionary<int, decimal>();

            // 1. Process explicit prices array if provided
            if (request.Prices != null)
            {
                foreach (var p in request.Prices)
                {
                    if (p.LocationId != 1 && p.LocationId != 2 && p.LocationId != 8)
                    {
                        return BadRequest($"Invalid location_id '{p.LocationId}'. Supported locations are 1 (TABLE), 2 (PARCEL), and 8 (ONLINE).");
                    }

                    if (p.Price < 0m)
                    {
                        return BadRequest($"Price for location {p.LocationId} cannot be negative.");
                    }

                    resolvedPrices[p.LocationId] = Math.Round(p.Price, 2);
                }
            }

            // 2. Process shortcut location properties if provided
            if (request.TablePrice.HasValue)
            {
                if (request.TablePrice.Value < 0m) return BadRequest("table_price cannot be negative.");
                resolvedPrices[1] = Math.Round(request.TablePrice.Value, 2);
            }
            if (request.ParcelPrice.HasValue)
            {
                if (request.ParcelPrice.Value < 0m) return BadRequest("parcel_price cannot be negative.");
                resolvedPrices[2] = Math.Round(request.ParcelPrice.Value, 2);
            }
            if (request.OnlinePrice.HasValue)
            {
                if (request.OnlinePrice.Value < 0m) return BadRequest("online_price cannot be negative.");
                resolvedPrices[8] = Math.Round(request.OnlinePrice.Value, 2);
            }

            // 3. Process fallback generic price if supplied
            if (request.Price.HasValue)
            {
                if (request.Price.Value < 0m) return BadRequest("price cannot be negative.");
                decimal fallback = Math.Round(request.Price.Value, 2);
                if (!resolvedPrices.ContainsKey(1)) resolvedPrices[1] = fallback;
                if (!resolvedPrices.ContainsKey(2)) resolvedPrices[2] = fallback;
                if (!resolvedPrices.ContainsKey(8)) resolvedPrices[8] = fallback;
            }

            if (resolvedPrices.Count == 0)
            {
                return BadRequest("At least one location price must be provided for locations 1 (TABLE), 2 (PARCEL), or 8 (ONLINE).");
            }

            try
            {
                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    // 1. Validate Category exists and is active
                    using (var catCmd = connection.CreateCommand())
                    {
                        catCmd.CommandText = "SELECT id FROM master_category WHERE id = @catId AND is_delete = 0 LIMIT 1;";
                        catCmd.Parameters.Add("@catId", MySqlDbType.Int32).Value = request.CategoryId;
                        object catResult = catCmd.ExecuteScalar();
                        if (catResult == null)
                        {
                            return BadRequest($"Category ID {request.CategoryId} does not exist or is inactive.");
                        }
                    }

                    // 2. Validate optional user_code if provided
                    int? userCode = request.UserCode;
                    if (userCode.HasValue && userCode.Value <= 0)
                    {
                        return BadRequest("user_code must be greater than zero if provided.");
                    }

                    // Base price for tblitems: use Location 1 price if present, else first resolved price
                    decimal basePrice = resolvedPrices.ContainsKey(1) ? resolvedPrices[1] : (resolvedPrices.Count > 0 ? new List<decimal>(resolvedPrices.Values)[0] : 0m);
                    DateTime now = DateTime.Now;
                    long newItemId;
                    var createdPricesDtoList = new List<ItemPriceDto>();

                    // 3. Transaction for item insertion and all price rows
                    using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            // A. Insert into tblitems
                            using (var insertItemCmd = connection.CreateCommand())
                            {
                                insertItemCmd.Transaction = transaction;
                                insertItemCmd.CommandText = @"
                                    INSERT INTO tblitems (
                                        name, price, units, cgst, sgst, discount, hsn,
                                        createdDate, updatedDate, updatedBy, isDelete,
                                        cgstAmount, sgstAmount, discountAmount, sellingPrice,
                                        tamil_name, user_code, category_id, brand_id,
                                        is_kot, is_discount, show_order
                                    ) VALUES (
                                        @name, @price, @units, 0, 0, 0, '',
                                        @createdDate, @createdDate, @updatedBy, 0,
                                        0, 0, 0, 0,
                                        @tamilName, @userCode, @categoryId, 3,
                                        @isKot, 0, @showOrder
                                    );";

                                insertItemCmd.Parameters.Add("@name", MySqlDbType.VarChar).Value = trimmedName;
                                insertItemCmd.Parameters.Add("@updatedBy", MySqlDbType.VarChar).Value = CurrentUser.Name(User);
                                insertItemCmd.Parameters.Add("@price", MySqlDbType.Double).Value = (double)basePrice;
                                insertItemCmd.Parameters.Add("@units", MySqlDbType.VarChar).Value = units;
                                insertItemCmd.Parameters.Add("@createdDate", MySqlDbType.DateTime).Value = now;
                                insertItemCmd.Parameters.Add("@tamilName", MySqlDbType.VarChar).Value = tamilName;
                                insertItemCmd.Parameters.Add("@userCode", MySqlDbType.Int32).Value = (object)userCode ?? DBNull.Value;
                                insertItemCmd.Parameters.Add("@categoryId", MySqlDbType.Int32).Value = request.CategoryId;
                                insertItemCmd.Parameters.Add("@isKot", MySqlDbType.Int32).Value = isKot;
                                insertItemCmd.Parameters.Add("@showOrder", MySqlDbType.Int32).Value = showOrder;

                                insertItemCmd.ExecuteNonQuery();
                                newItemId = insertItemCmd.LastInsertedId;
                            }

                            // If user_code was not supplied, set user_code = newItemId
                            if (!userCode.HasValue)
                            {
                                userCode = (int)newItemId;
                                using (var updateCodeCmd = connection.CreateCommand())
                                {
                                    updateCodeCmd.Transaction = transaction;
                                    updateCodeCmd.CommandText = "UPDATE tblitems SET user_code = @code WHERE id = @id;";
                                    updateCodeCmd.Parameters.Add("@code", MySqlDbType.Int32).Value = userCode.Value;
                                    updateCodeCmd.Parameters.Add("@id", MySqlDbType.Int32).Value = (int)newItemId;
                                    updateCodeCmd.ExecuteNonQuery();
                                }
                            }

                            // B. Insert price rows into tblitemsprice
                            foreach (var kvp in resolvedPrices)
                            {
                                using (var insertPriceCmd = connection.CreateCommand())
                                {
                                    insertPriceCmd.Transaction = transaction;
                                    insertPriceCmd.CommandText = @"
                                        INSERT INTO tblitemsprice (
                                            location_id, item_id, price
                                        ) VALUES (
                                            @locId, @itemId, @price
                                        );";

                                    insertPriceCmd.Parameters.Add("@locId", MySqlDbType.Int32).Value = kvp.Key;
                                    insertPriceCmd.Parameters.Add("@itemId", MySqlDbType.Int32).Value = (int)newItemId;
                                    insertPriceCmd.Parameters.Add("@price", MySqlDbType.Double).Value = (double)kvp.Value;

                                    insertPriceCmd.ExecuteNonQuery();
                                    long priceId = insertPriceCmd.LastInsertedId;

                                    createdPricesDtoList.Add(new ItemPriceDto
                                    {
                                        Id = (int)priceId,
                                        ItemId = (int)newItemId,
                                        LocationId = kvp.Key,
                                        Price = kvp.Value
                                    });
                                }
                            }

                            transaction.Commit();
                        }
                        catch
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }

                    // 4. Return CreateItemResponseDto with 201 Created
                    var responseDto = new CreateItemResponseDto
                    {
                        Id = (int)newItemId,
                        Name = trimmedName,
                        CategoryId = request.CategoryId,
                        Units = units,
                        UserCode = userCode,
                        TamilName = tamilName,
                        IsKot = isKot,
                        ShowOrder = showOrder,
                        Prices = createdPricesDtoList
                    };

                    return Content(System.Net.HttpStatusCode.Created, responseDto);
                }
            }
            catch (Exception ex)
            {
                Db.LogError("Items", ex);
                return InternalServerError();
            }
        }
    }
}
