using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using BillerRestoApi.Data;
using BillerRestoApi.Security;
using System.Text;
using System.Web.Http;
using BillerRestoApi.Models;

namespace BillerRestoApi.Controllers
{
    [RoutePrefix("api/bills")]
    public class BillsController : ApiController
    {
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetBills(
            [FromUri] int? billNo = null,
            [FromUri] DateTime? startDate = null,
            [FromUri] DateTime? endDate = null,
            [FromUri] string billType = null,
            [FromUri] string tableNo = null,
            [FromUri] int? limit = null,
            [FromUri] int page = 1,
            [FromUri] int pageSize = 50)

        {
            // Validate query parameters


            if (page < 1)
            {
                return BadRequest("The 'page' parameter must be at least 1.");
            }

            if (pageSize < 1 || pageSize > 200)
            {
                return BadRequest("The 'pageSize' parameter must be between 1 and 200.");
            }

            if (limit.HasValue && (limit.Value < 1 || limit.Value > 200))
            {
                return BadRequest("The 'limit' parameter must be an integer between 1 and 200.");
            }

            if (startDate.HasValue && endDate.HasValue && startDate.Value.Date > endDate.Value.Date)
            {
                return BadRequest("The 'startDate' cannot be greater than 'endDate'.");
            }

            if (!string.IsNullOrWhiteSpace(billType))
            {
                string normalizedType = billType.Trim().ToLowerInvariant();
                if (normalizedType != "kot" && normalizedType != "ta" && normalizedType != "online")
                {
                    return BadRequest("The 'billType' parameter must be 'kot', 'ta', or 'online'.");
                }
            }

            try
            {
                int effectiveLimit = limit ?? pageSize;
                int offset = (page - 1) * effectiveLimit;


                // Build derived inner query to limit headers BEFORE joining line items,
                // ensuring no bill's item list is truncated by the limit.
                var innerQueryBuilder = new StringBuilder(@"
                    SELECT 
                        id, billNo, billDate, customerName, phone, address,
                        payment_mode, totalItems, subTotal, discount, roundOff,
                        netTotal, amount_recevied, balance_amount, payment_reference,
                        is_paid, paid_on, table_no, part, bill_type, waiter_id,
                        custGST, createdBy, createdDate
                    FROM tblbills
                    WHERE isDelete = @isDelete");

                if (billNo.HasValue)
                {
                    innerQueryBuilder.Append(" AND billNo = @billNo");
                }

                if (startDate.HasValue)
                {
                    innerQueryBuilder.Append(" AND billDate >= @startDate");
                }

                if (endDate.HasValue)
                {
                    innerQueryBuilder.Append(" AND billDate < @endDateExclusive");
                }

                if (!string.IsNullOrWhiteSpace(billType))
                {
                    innerQueryBuilder.Append(" AND bill_type = @billType");
                }

                if (!string.IsNullOrWhiteSpace(tableNo))
                {
                    innerQueryBuilder.Append(" AND table_no = @tableNo");
                }


                innerQueryBuilder.Append(" ORDER BY id DESC LIMIT @limit OFFSET @offset");


                string query = $@"
                    SELECT 
                        b.id, b.billNo, b.billDate, b.customerName, b.phone, b.address,
                        b.payment_mode, b.totalItems, b.subTotal, b.discount, b.roundOff,
                        b.netTotal, b.amount_recevied, b.balance_amount, b.payment_reference,
                        b.is_paid, b.paid_on, b.table_no, b.part, b.bill_type, b.waiter_id,
                        b.custGST, b.createdBy, b.createdDate,
                        i.id AS line_id, i.itemId, i.itemName, i.price, i.quantity, i.units,
                        i.totalAmount, i.user_code, i.tamil_name
                    FROM ({innerQueryBuilder}) b
                    LEFT JOIN tblbillitems i ON b.id = i.billId AND i.is_delete = 0
                    ORDER BY b.id DESC, i.id ASC;";

                var billsDict = new Dictionary<int, BillDto>();

                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.Parameters.Add("@isDelete", MySqlDbType.Int32).Value = 0;
                        command.Parameters.Add("@limit", MySqlDbType.Int32).Value = effectiveLimit;

                        command.Parameters.Add("@offset", MySqlDbType.Int32).Value = offset;


                        if (billNo.HasValue)
                        {
                            command.Parameters.Add("@billNo", MySqlDbType.Int32).Value = billNo.Value;
                        }

                        if (startDate.HasValue)
                        {
                            command.Parameters.Add("@startDate", MySqlDbType.DateTime).Value = startDate.Value.Date;
                        }

                        if (endDate.HasValue)
                        {
                            // Include the full endDate day via exclusive upper bound of the following day
                            command.Parameters.Add("@endDateExclusive", MySqlDbType.DateTime).Value = endDate.Value.Date.AddDays(1);
                        }

                        if (!string.IsNullOrWhiteSpace(billType))
                        {
                            command.Parameters.Add("@billType", MySqlDbType.VarChar).Value = billType.Trim().ToLowerInvariant();
                        }

                        if (!string.IsNullOrWhiteSpace(tableNo))
                        {
                            command.Parameters.Add("@tableNo", MySqlDbType.VarChar).Value = tableNo.Trim();
                        }

                        using (var reader = command.ExecuteReader())
                        {
                            int idOrdinal = reader.GetOrdinal("id");
                            int billNoOrdinal = reader.GetOrdinal("billNo");
                            int billDateOrdinal = reader.GetOrdinal("billDate");
                            int customerNameOrdinal = reader.GetOrdinal("customerName");
                            int phoneOrdinal = reader.GetOrdinal("phone");
                            int addressOrdinal = reader.GetOrdinal("address");
                            int paymentModeOrdinal = reader.GetOrdinal("payment_mode");
                            int totalItemsOrdinal = reader.GetOrdinal("totalItems");
                            int subTotalOrdinal = reader.GetOrdinal("subTotal");
                            int discountOrdinal = reader.GetOrdinal("discount");
                            int roundOffOrdinal = reader.GetOrdinal("roundOff");
                            int netTotalOrdinal = reader.GetOrdinal("netTotal");
                            int amountReceviedOrdinal = reader.GetOrdinal("amount_recevied");
                            int balanceAmountOrdinal = reader.GetOrdinal("balance_amount");
                            int paymentRefOrdinal = reader.GetOrdinal("payment_reference");
                            int isPaidOrdinal = reader.GetOrdinal("is_paid");
                            int paidOnOrdinal = reader.GetOrdinal("paid_on");
                            int tableNoOrdinal = reader.GetOrdinal("table_no");
                            int partOrdinal = reader.GetOrdinal("part");
                            int billTypeOrdinal = reader.GetOrdinal("bill_type");
                            int waiterIdOrdinal = reader.GetOrdinal("waiter_id");
                            int custGstOrdinal = reader.GetOrdinal("custGST");
                            int createdByOrdinal = reader.GetOrdinal("createdBy");
                            int createdDateOrdinal = reader.GetOrdinal("createdDate");

                            int lineIdOrdinal = reader.GetOrdinal("line_id");
                            int itemIdOrdinal = reader.GetOrdinal("itemId");
                            int itemNameOrdinal = reader.GetOrdinal("itemName");
                            int priceOrdinal = reader.GetOrdinal("price");
                            int quantityOrdinal = reader.GetOrdinal("quantity");
                            int unitsOrdinal = reader.GetOrdinal("units");
                            int totalAmountOrdinal = reader.GetOrdinal("totalAmount");
                            int userCodeOrdinal = reader.GetOrdinal("user_code");
                            int tamilNameOrdinal = reader.GetOrdinal("tamil_name");

                            while (reader.Read())
                            {
                                int billId = Convert.ToInt32(reader[idOrdinal]);

                                if (!billsDict.TryGetValue(billId, out var bill))
                                {
                                    bill = new BillDto
                                    {
                                        Id = billId,
                                        BillNo = Convert.ToInt32(reader[billNoOrdinal]),
                                        BillDate = reader.IsDBNull(billDateOrdinal) ? (DateTime?)null : Convert.ToDateTime(reader[billDateOrdinal]),
                                        CustomerName = reader.IsDBNull(customerNameOrdinal) ? string.Empty : reader[customerNameOrdinal].ToString(),
                                        Phone = reader.IsDBNull(phoneOrdinal) ? string.Empty : reader[phoneOrdinal].ToString(),
                                        Address = reader.IsDBNull(addressOrdinal) ? string.Empty : reader[addressOrdinal].ToString(),
                                        PaymentMode = reader.IsDBNull(paymentModeOrdinal) ? string.Empty : reader[paymentModeOrdinal].ToString(),
                                        TotalItems = reader.IsDBNull(totalItemsOrdinal) ? (int?)null : Convert.ToInt32(reader[totalItemsOrdinal]),
                                        SubTotal = reader.IsDBNull(subTotalOrdinal) ? 0m : Convert.ToDecimal(reader[subTotalOrdinal]),
                                        Discount = reader.IsDBNull(discountOrdinal) ? 0m : Convert.ToDecimal(reader[discountOrdinal]),
                                        RoundOff = reader.IsDBNull(roundOffOrdinal) ? (decimal?)null : Convert.ToDecimal(reader[roundOffOrdinal]),
                                        NetTotal = reader.IsDBNull(netTotalOrdinal) ? 0m : Convert.ToDecimal(reader[netTotalOrdinal]),
                                        AmountRecevied = reader.IsDBNull(amountReceviedOrdinal) ? (decimal?)null : Convert.ToDecimal(reader[amountReceviedOrdinal]),
                                        BalanceAmount = reader.IsDBNull(balanceAmountOrdinal) ? (decimal?)null : Convert.ToDecimal(reader[balanceAmountOrdinal]),
                                        PaymentReference = reader.IsDBNull(paymentRefOrdinal) ? string.Empty : reader[paymentRefOrdinal].ToString(),
                                        IsPaid = reader.IsDBNull(isPaidOrdinal) ? 0 : Convert.ToInt32(reader[isPaidOrdinal]),
                                        PaidOn = reader.IsDBNull(paidOnOrdinal) ? (DateTime?)null : Convert.ToDateTime(reader[paidOnOrdinal]),
                                        TableNo = reader.IsDBNull(tableNoOrdinal) ? string.Empty : reader[tableNoOrdinal].ToString(),
                                        Part = reader.IsDBNull(partOrdinal) ? string.Empty : reader[partOrdinal].ToString(),
                                        BillType = reader.IsDBNull(billTypeOrdinal) ? string.Empty : reader[billTypeOrdinal].ToString(),
                                        WaiterId = reader.IsDBNull(waiterIdOrdinal) ? (int?)null : Convert.ToInt32(reader[waiterIdOrdinal]),
                                        CustGst = reader.IsDBNull(custGstOrdinal) ? string.Empty : reader[custGstOrdinal].ToString(),
                                        CreatedBy = reader.IsDBNull(createdByOrdinal) ? string.Empty : reader[createdByOrdinal].ToString(),
                                        CreatedDate = reader.IsDBNull(createdDateOrdinal) ? (DateTime?)null : Convert.ToDateTime(reader[createdDateOrdinal]),
                                        Items = new List<BillItemDto>()
                                    };

                                    billsDict.Add(billId, bill);
                                }

                                if (!reader.IsDBNull(lineIdOrdinal))
                                {
                                    var item = new BillItemDto
                                    {
                                        Id = Convert.ToInt32(reader[lineIdOrdinal]),
                                        BillId = billId,
                                        ItemId = reader.IsDBNull(itemIdOrdinal) ? (int?)null : Convert.ToInt32(reader[itemIdOrdinal]),
                                        ItemName = reader.IsDBNull(itemNameOrdinal) ? string.Empty : reader[itemNameOrdinal].ToString(),
                                        Price = reader.IsDBNull(priceOrdinal) ? 0m : Convert.ToDecimal(reader[priceOrdinal]),
                                        Quantity = reader.IsDBNull(quantityOrdinal) ? 0m : Convert.ToDecimal(reader[quantityOrdinal]),
                                        Units = reader.IsDBNull(unitsOrdinal) ? string.Empty : reader[unitsOrdinal].ToString(),
                                        TotalAmount = reader.IsDBNull(totalAmountOrdinal) ? 0m : Convert.ToDecimal(reader[totalAmountOrdinal]),
                                        UserCode = reader.IsDBNull(userCodeOrdinal) ? string.Empty : reader[userCodeOrdinal].ToString(),
                                        TamilName = reader.IsDBNull(tamilNameOrdinal) ? string.Empty : reader[tamilNameOrdinal].ToString()
                                    };

                                    bill.Items.Add(item);
                                }
                            }
                        }
                    }
                }

                return Ok(billsDict.Values);
            }
            catch (Exception ex)
            {
                Db.LogError("Bills", ex);
                // Return generic 500 without exposing connection strings, credentials, or internal exception details
                return InternalServerError();
            }
        }

        [HttpPost]
        [Route("")]
        public IHttpActionResult CreateBill([FromBody] CreateBillRequestDto request)
        {
            if (request == null)
            {
                return BadRequest("Request body cannot be empty.");
            }

            // 1. Validate Location: 1 = TABLE, 2 = PARCEL, 8 = ONLINE
            if (request.LocationId != 1 && request.LocationId != 2 && request.LocationId != 8)
            {
                return BadRequest($"Invalid location_id '{request.LocationId}'. Supported locations are 1 (Dine-in/TABLE), 2 (Takeaway/PARCEL), and 8 (Delivery/ONLINE).");
            }

            // 2. Validate and resolve bill_type
            string resolvedBillType;
            if (string.IsNullOrWhiteSpace(request.BillType))
            {
                resolvedBillType = request.LocationId == 1 ? "kot" : (request.LocationId == 2 ? "ta" : "online");
            }
            else
            {
                string normalizedType = request.BillType.Trim().ToLowerInvariant();
                if (normalizedType != "kot" && normalizedType != "ta" && normalizedType != "online")
                {
                    return BadRequest("The 'bill_type' must be 'kot', 'ta', or 'online'.");
                }

                if (request.LocationId == 1 && normalizedType != "kot")
                {
                    return BadRequest("Location 1 (Dine-in) must use bill_type 'kot'.");
                }

                if (request.LocationId == 2 && normalizedType != "ta")
                {
                    return BadRequest("Location 2 (Takeaway/Parcel) must use bill_type 'ta'.");
                }

                if (request.LocationId == 8 && normalizedType != "online" && normalizedType != "ta")
                {
                    return BadRequest("Location 8 (Online) must use bill_type 'online' or 'ta'.");
                }

                resolvedBillType = normalizedType;
            }

            // 3. Validate items array
            if (request.Items == null || request.Items.Count == 0)
            {
                return BadRequest("The bill must contain at least one item.");
            }

            // Consolidate duplicate item rows if supplied
            var consolidatedItems = new Dictionary<int, decimal>();
            foreach (var item in request.Items)
            {
                if (item.ItemId <= 0)
                {
                    return BadRequest("Each item_id must be greater than zero.");
                }

                if (item.Quantity <= 0)
                {
                    return BadRequest($"Item ID {item.ItemId} has invalid quantity '{item.Quantity}'. Quantity must be greater than zero.");
                }

                if (consolidatedItems.ContainsKey(item.ItemId))
                {
                    consolidatedItems[item.ItemId] += item.Quantity;
                }
                else
                {
                    consolidatedItems[item.ItemId] = item.Quantity;
                }
            }

            try
            {
                using (var connection = Db.CreateConnection())
                {
                    connection.Open();

                    // 4. Validate Table and Waiter for Dine-in
                    string tableNo = string.Empty;
                    int? waiterId = null;

                    if (request.LocationId == 1)
                    {
                        if (string.IsNullOrWhiteSpace(request.TableNo))
                        {
                            return BadRequest("A valid table_no is required for Dine-in (Location 1) orders.");
                        }

                        tableNo = request.TableNo.Trim();

                        using (var tableCmd = connection.CreateCommand())
                        {
                            tableCmd.CommandText = "SELECT id FROM master_table WHERE name = @tableName AND is_delete = 0 LIMIT 1;";
                            tableCmd.Parameters.Add("@tableName", MySqlDbType.VarChar).Value = tableNo;
                            object tableResult = tableCmd.ExecuteScalar();
                            if (tableResult == null)
                            {
                                return BadRequest($"Table '{tableNo}' does not exist or is inactive.");
                            }
                        }

                        if (request.WaiterId.HasValue)
                        {
                            using (var waiterCmd = connection.CreateCommand())
                            {
                                waiterCmd.CommandText = "SELECT id FROM master_waiter WHERE id = @waiterId AND is_delete = 0 LIMIT 1;";
                                waiterCmd.Parameters.Add("@waiterId", MySqlDbType.Int32).Value = request.WaiterId.Value;
                                object waiterResult = waiterCmd.ExecuteScalar();
                                if (waiterResult == null)
                                {
                                    return BadRequest($"Waiter ID {request.WaiterId.Value} does not exist or is inactive.");
                                }
                            }
                            waiterId = request.WaiterId.Value;
                        }
                    }
                    else
                    {
                        // Parcel and Online orders do not require table or waiter
                        tableNo = request.TableNo?.Trim() ?? string.Empty;
                        waiterId = request.WaiterId;
                    }

                    // 5. Query and validate items and location-specific prices
                    var itemDetailsList = new List<ValidatedLineItem>();
                    var itemIdsList = new List<int>(consolidatedItems.Keys);

                    var inClauseBuilder = new StringBuilder();
                    using (var itemsCmd = connection.CreateCommand())
                    {
                        itemsCmd.Parameters.Add("@locationId", MySqlDbType.Int32).Value = request.LocationId;
                        for (int i = 0; i < itemIdsList.Count; i++)
                        {
                            if (i > 0) inClauseBuilder.Append(",");
                            string paramName = "@id" + i;
                            inClauseBuilder.Append(paramName);
                            itemsCmd.Parameters.Add(paramName, MySqlDbType.Int32).Value = itemIdsList[i];
                        }

                        itemsCmd.CommandText = $@"
                            SELECT 
                                i.id, i.name, i.units, i.user_code, i.tamil_name, i.isDelete, p.price
                            FROM tblitems i
                            LEFT JOIN tblitemsprice p ON i.id = p.item_id AND p.location_id = @locationId
                            WHERE i.id IN ({inClauseBuilder})";

                        using (var reader = itemsCmd.ExecuteReader())
                        {
                            var foundItemIds = new HashSet<int>();
                            while (reader.Read())
                            {
                                int id = Convert.ToInt32(reader["id"]);
                                int isDelete = Convert.ToInt32(reader["isDelete"]);
                                string name = reader["name"]?.ToString() ?? string.Empty;

                                if (isDelete != 0)
                                {
                                    return BadRequest($"Item '{name}' (ID {id}) is inactive or has been deleted.");
                                }

                                if (reader.IsDBNull(reader.GetOrdinal("price")))
                                {
                                    return BadRequest($"Item '{name}' (ID {id}) does not have an active price configured for location {request.LocationId}.");
                                }

                                decimal price = Convert.ToDecimal(reader["price"]);
                                string units = reader.IsDBNull(reader.GetOrdinal("units")) ? string.Empty : reader["units"].ToString();
                                string userCode = reader.IsDBNull(reader.GetOrdinal("user_code")) ? string.Empty : reader["user_code"].ToString();
                                string tamilName = reader.IsDBNull(reader.GetOrdinal("tamil_name")) ? string.Empty : reader["tamil_name"].ToString();
                                decimal quantity = consolidatedItems[id];
                                decimal totalAmount = Math.Round(price * quantity, 2);

                                foundItemIds.Add(id);
                                itemDetailsList.Add(new ValidatedLineItem
                                {
                                    ItemId = id,
                                    ItemName = name,
                                    Price = price,
                                    Quantity = quantity,
                                    Units = units,
                                    TotalAmount = totalAmount,
                                    UserCode = userCode,
                                    TamilName = tamilName
                                });
                            }

                            foreach (var requestedId in itemIdsList)
                            {
                                if (!foundItemIds.Contains(requestedId))
                                {
                                    return BadRequest($"Item ID {requestedId} was not found in active menu items.");
                                }
                            }
                        }
                    }

                    // 6. Totals Calculation (Non-GST entity: netTotal = subTotal - discount + roundOff)
                    decimal subTotal = 0m;
                    foreach (var line in itemDetailsList)
                    {
                        subTotal += line.TotalAmount;
                    }
                    subTotal = Math.Round(subTotal, 2);

                    decimal discount = Math.Max(0m, request.Discount.GetValueOrDefault(0m));
                    if (discount > subTotal)
                    {
                        return BadRequest("Discount cannot be greater than subtotal.");
                    }

                    // Round-off is a client-supplied adjustment; bound it so it cannot be used to zero out a bill.
            decimal roundOff = request.RoundOff.GetValueOrDefault(0m);
            if (roundOff < -1m || roundOff > 1m)
            {
                return BadRequest("round_off must be between -1 and 1.");
            }
                    decimal netTotal = Math.Round(subTotal - discount + roundOff, 2);
                    if (netTotal < 0m)
                    {
                        return BadRequest("Net total cannot be negative.");
                    }

                    // 7. Payment Fields
                    string paymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? "CASH" : request.PaymentMode.Trim().ToUpperInvariant();
                    int isPaid = request.IsPaid ? 1 : 0;
                    DateTime billDate = DateTime.Now;
                    DateTime? paidOn = isPaid == 1 ? (DateTime?)billDate : null;
                    decimal? amountRecevied = null;
                    decimal? balanceAmount = null;

                    if (isPaid == 1)
                    {
                        amountRecevied = request.AmountRecevied ?? netTotal;
                        if (amountRecevied.Value < netTotal)
                        {
                            return BadRequest("amount_recevied cannot be less than netTotal for a paid bill.");
                        }
                        balanceAmount = Math.Round(amountRecevied.Value - netTotal, 2);
                    }

                    string customerName = request.CustomerName?.Trim() ?? string.Empty;
                    string phone = request.Phone?.Trim() ?? string.Empty;
                    string address = request.Address?.Trim() ?? string.Empty;
                    string createdBy = CurrentUser.Name(User); // trusted identity from the token, not the request body
                    string part = request.Part?.Trim() ?? (request.LocationId == 1 ? "A" : string.Empty);
                    string paymentRef = request.PaymentReference?.Trim() ?? string.Empty;

                    // 8. Atomic Transaction: Lock Sequence, Allocate Bill Number, Insert Header & Items
                    int assignedBillNo;
                    long newBillId;
                    var createdItemsDtoList = new List<BillItemDto>();

                    using (var transaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                    {
                        try
                        {
                            // A. Row Lock on tblbillno for companyId = 1
                            int seqId;
                            using (var lockCmd = connection.CreateCommand())
                            {
                                lockCmd.Transaction = transaction;
                                lockCmd.CommandText = "SELECT id, billNo FROM tblbillno WHERE companyId = 1 LIMIT 1 FOR UPDATE;";
                                using (var lockReader = lockCmd.ExecuteReader())
                                {
                                    if (!lockReader.Read())
                                    {
                                        throw new InvalidOperationException("Bill sequence record for companyId = 1 not found in tblbillno.");
                                    }
                                    seqId = Convert.ToInt32(lockReader["id"]);
                                    assignedBillNo = Convert.ToInt32(lockReader["billNo"]);
                                }
                            }

                            // B. Update sequence counter
                            int nextBillNo = assignedBillNo + 1;
                            using (var updateSeqCmd = connection.CreateCommand())
                            {
                                updateSeqCmd.Transaction = transaction;
                                updateSeqCmd.CommandText = "UPDATE tblbillno SET billNo = @nextBillNo, last_update = @updateDate WHERE id = @seqId;";
                                updateSeqCmd.Parameters.Add("@nextBillNo", MySqlDbType.Int32).Value = nextBillNo;
                                updateSeqCmd.Parameters.Add("@updateDate", MySqlDbType.DateTime).Value = billDate;
                                updateSeqCmd.Parameters.Add("@seqId", MySqlDbType.Int32).Value = seqId;
                                int rowsAffected = updateSeqCmd.ExecuteNonQuery();
                                if (rowsAffected != 1)
                                {
                                    throw new InvalidOperationException("Failed to update tblbillno sequence counter.");
                                }
                            }

                            // C. Insert bill header into tblbills
                            using (var insertBillCmd = connection.CreateCommand())
                            {
                                insertBillCmd.Transaction = transaction;
                                insertBillCmd.CommandText = @"
                                    INSERT INTO tblbills (
                                        billNo, billDate, customerName, phone, address,
                                        paymentMode, totalItems, subTotal, discount, roundOff,
                                        netTotal, isDelete, createdBy, createdDate, companyId,
                                        custGST, salesman, companyName, waiter_id, bill_type,
                                        delete_reason, is_paid, paid_on, table_no, part,
                                        amount_recevied, balance_amount, payment_mode, payment_reference
                                    ) VALUES (
                                        @billNo, @billDate, @customerName, @phone, @address,
                                        @paymentMode, @totalItems, @subTotal, @discount, @roundOff,
                                        @netTotal, 0, @createdBy, @createdDate, 1,
                                        '', '', 'PARK RESTAURANT', @waiterId, @billType,
                                        '', @isPaid, @paidOn, @tableNo, @part,
                                        @amountRecevied, @balanceAmount, @paymentMode, @paymentReference
                                    );";

                                insertBillCmd.Parameters.Add("@billNo", MySqlDbType.Int32).Value = assignedBillNo;
                                insertBillCmd.Parameters.Add("@billDate", MySqlDbType.DateTime).Value = billDate;
                                insertBillCmd.Parameters.Add("@customerName", MySqlDbType.VarChar).Value = customerName;
                                insertBillCmd.Parameters.Add("@phone", MySqlDbType.VarChar).Value = phone;
                                insertBillCmd.Parameters.Add("@address", MySqlDbType.VarChar).Value = address;
                                insertBillCmd.Parameters.Add("@paymentMode", MySqlDbType.VarChar).Value = paymentMode;
                                insertBillCmd.Parameters.Add("@totalItems", MySqlDbType.Int32).Value = itemDetailsList.Count;
                                insertBillCmd.Parameters.Add("@subTotal", MySqlDbType.Double).Value = (double)subTotal;
                                insertBillCmd.Parameters.Add("@discount", MySqlDbType.Double).Value = (double)discount;
                                insertBillCmd.Parameters.Add("@roundOff", MySqlDbType.Double).Value = (double)roundOff;
                                insertBillCmd.Parameters.Add("@netTotal", MySqlDbType.Double).Value = (double)netTotal;
                                insertBillCmd.Parameters.Add("@createdBy", MySqlDbType.VarChar).Value = createdBy;
                                insertBillCmd.Parameters.Add("@createdDate", MySqlDbType.DateTime).Value = billDate;
                                insertBillCmd.Parameters.Add("@waiterId", MySqlDbType.Int32).Value = (object)waiterId ?? DBNull.Value;
                                insertBillCmd.Parameters.Add("@billType", MySqlDbType.VarChar).Value = resolvedBillType;
                                insertBillCmd.Parameters.Add("@isPaid", MySqlDbType.Int32).Value = isPaid;
                                insertBillCmd.Parameters.Add("@paidOn", MySqlDbType.DateTime).Value = (object)paidOn ?? DBNull.Value;
                                insertBillCmd.Parameters.Add("@tableNo", MySqlDbType.VarChar).Value = tableNo;
                                insertBillCmd.Parameters.Add("@part", MySqlDbType.VarChar).Value = part;
                                insertBillCmd.Parameters.Add("@amountRecevied", MySqlDbType.Double).Value = (object)amountRecevied ?? DBNull.Value;
                                insertBillCmd.Parameters.Add("@balanceAmount", MySqlDbType.Double).Value = (object)balanceAmount ?? DBNull.Value;
                                insertBillCmd.Parameters.Add("@paymentReference", MySqlDbType.VarChar).Value = paymentRef;

                                insertBillCmd.ExecuteNonQuery();
                                newBillId = insertBillCmd.LastInsertedId;
                            }

                            // D. Insert bill line items into tblbillitems
                            foreach (var line in itemDetailsList)
                            {
                                using (var insertLineCmd = connection.CreateCommand())
                                {
                                    insertLineCmd.Transaction = transaction;
                                    insertLineCmd.CommandText = @"
                                        INSERT INTO tblbillitems (
                                            billId, itemId, itemName, price, quantity,
                                            units, totalAmount, user_code, tamil_name,
                                            billDate, companyId, is_delete, bill_type
                                        ) VALUES (
                                            @billId, @itemId, @itemName, @price, @quantity,
                                            @units, @totalAmount, @userCode, @tamilName,
                                            @billDate, 1, 0, @billType
                                        );";

                                    insertLineCmd.Parameters.Add("@billId", MySqlDbType.Int32).Value = (int)newBillId;
                                    insertLineCmd.Parameters.Add("@itemId", MySqlDbType.Int32).Value = line.ItemId;
                                    insertLineCmd.Parameters.Add("@itemName", MySqlDbType.VarChar).Value = line.ItemName;
                                    insertLineCmd.Parameters.Add("@price", MySqlDbType.Double).Value = (double)line.Price;
                                    insertLineCmd.Parameters.Add("@quantity", MySqlDbType.Double).Value = (double)line.Quantity;
                                    insertLineCmd.Parameters.Add("@units", MySqlDbType.VarChar).Value = line.Units;
                                    insertLineCmd.Parameters.Add("@totalAmount", MySqlDbType.Double).Value = (double)line.TotalAmount;
                                    insertLineCmd.Parameters.Add("@userCode", MySqlDbType.VarChar).Value = line.UserCode;
                                    insertLineCmd.Parameters.Add("@tamilName", MySqlDbType.VarChar).Value = line.TamilName;
                                    insertLineCmd.Parameters.Add("@billDate", MySqlDbType.DateTime).Value = billDate;
                                    insertLineCmd.Parameters.Add("@billType", MySqlDbType.VarChar).Value = resolvedBillType;

                                    insertLineCmd.ExecuteNonQuery();
                                    long lineId = insertLineCmd.LastInsertedId;

                                    createdItemsDtoList.Add(new BillItemDto
                                    {
                                        Id = (int)lineId,
                                        BillId = (int)newBillId,
                                        ItemId = line.ItemId,
                                        ItemName = line.ItemName,
                                        Price = line.Price,
                                        Quantity = line.Quantity,
                                        Units = line.Units,
                                        TotalAmount = line.TotalAmount,
                                        UserCode = line.UserCode,
                                        TamilName = line.TamilName
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

                    // 9. Build and return created BillDto with 201 Created
                    var responseDto = new BillDto
                    {
                        Id = (int)newBillId,
                        BillNo = assignedBillNo,
                        BillDate = billDate,
                        CustomerName = customerName,
                        Phone = phone,
                        Address = address,
                        PaymentMode = paymentMode,
                        TotalItems = itemDetailsList.Count,
                        SubTotal = subTotal,
                        Discount = discount,
                        RoundOff = roundOff,
                        NetTotal = netTotal,
                        AmountRecevied = amountRecevied,
                        BalanceAmount = balanceAmount,
                        PaymentReference = paymentRef,
                        IsPaid = isPaid,
                        PaidOn = paidOn,
                        TableNo = tableNo,
                        Part = part,
                        BillType = resolvedBillType,
                        WaiterId = waiterId,
                        CustGst = string.Empty,
                        CreatedBy = createdBy,
                        CreatedDate = billDate,
                        Items = createdItemsDtoList
                    };

                    return Content(System.Net.HttpStatusCode.Created, responseDto);
                }
            }
            catch (Exception ex)
            {
                Db.LogError("Bills", ex);
                return InternalServerError();
            }
        }

        private class ValidatedLineItem
        {
            public int ItemId { get; set; }
            public string ItemName { get; set; }
            public decimal Price { get; set; }
            public decimal Quantity { get; set; }
            public string Units { get; set; }
            public decimal TotalAmount { get; set; }
            public string UserCode { get; set; }
            public string TamilName { get; set; }
        }
    }
}
