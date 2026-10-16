using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class BillDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("bill_no")]
        public int BillNo { get; set; }

        [JsonProperty("bill_date")]
        public DateTime? BillDate { get; set; }

        [JsonProperty("customer_name")]
        public string CustomerName { get; set; }

        [JsonProperty("phone")]
        public string Phone { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("payment_mode")]
        public string PaymentMode { get; set; }

        [JsonProperty("total_items")]
        public int? TotalItems { get; set; }

        [JsonProperty("sub_total")]
        public decimal SubTotal { get; set; }

        [JsonProperty("discount")]
        public decimal Discount { get; set; }

        [JsonProperty("round_off")]
        public decimal? RoundOff { get; set; }

        [JsonProperty("net_total")]
        public decimal NetTotal { get; set; }

        // Preserved exact database column name with historical spelling
        [JsonProperty("amount_recevied")]
        public decimal? AmountRecevied { get; set; }

        [JsonProperty("balance_amount")]
        public decimal? BalanceAmount { get; set; }

        [JsonProperty("payment_reference")]
        public string PaymentReference { get; set; }

        [JsonProperty("is_paid")]
        public int IsPaid { get; set; }

        [JsonProperty("paid_on")]
        public DateTime? PaidOn { get; set; }

        [JsonProperty("table_no")]
        public string TableNo { get; set; }

        [JsonProperty("part")]
        public string Part { get; set; }

        [JsonProperty("bill_type")]
        public string BillType { get; set; }

        [JsonProperty("waiter_id")]
        public int? WaiterId { get; set; }

        [JsonProperty("cust_gst")]
        public string CustGst { get; set; }

        [JsonProperty("created_by")]
        public string CreatedBy { get; set; }

        [JsonProperty("created_date")]
        public DateTime? CreatedDate { get; set; }

        [JsonProperty("items")]
        public List<BillItemDto> Items { get; set; } = new List<BillItemDto>();
    }
}
