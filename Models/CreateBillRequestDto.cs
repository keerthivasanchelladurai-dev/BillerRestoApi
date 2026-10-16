using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class CreateBillItemRequestDto
    {
        [JsonProperty("item_id")]
        public int ItemId { get; set; }

        [JsonProperty("itemId")]
        private int ItemIdCamel { set => ItemId = value; }

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }
    }

    public class CreateBillRequestDto
    {
        [JsonProperty("location_id")]
        public int LocationId { get; set; }

        [JsonProperty("locationId")]
        private int LocationIdCamel { set => LocationId = value; }

        [JsonProperty("bill_type")]
        public string BillType { get; set; }

        [JsonProperty("billType")]
        private string BillTypeCamel { set => BillType = value; }

        [JsonProperty("table_no")]
        public string TableNo { get; set; }

        [JsonProperty("tableNo")]
        private string TableNoCamel { set => TableNo = value; }

        [JsonProperty("waiter_id")]
        public int? WaiterId { get; set; }

        [JsonProperty("waiterId")]
        private int? WaiterIdCamel { set => WaiterId = value; }

        [JsonProperty("customer_name")]
        public string CustomerName { get; set; }

        [JsonProperty("customerName")]
        private string CustomerNameCamel { set => CustomerName = value; }

        [JsonProperty("phone")]
        public string Phone { get; set; }

        [JsonProperty("address")]
        public string Address { get; set; }

        [JsonProperty("payment_mode")]
        public string PaymentMode { get; set; } = "CASH";

        [JsonProperty("paymentMode")]
        private string PaymentModeCamel { set => PaymentMode = value; }

        [JsonProperty("is_paid")]
        public bool IsPaid { get; set; } = true;

        [JsonProperty("isPaid")]
        private bool IsPaidCamel { set => IsPaid = value; }

        [JsonProperty("amount_recevied")]
        public decimal? AmountRecevied { get; set; }

        [JsonProperty("amountReceived")]
        private decimal? AmountReceivedCamel { set => AmountRecevied = value; }

        [JsonProperty("payment_reference")]
        public string PaymentReference { get; set; }

        [JsonProperty("paymentReference")]
        private string PaymentReferenceCamel { set => PaymentReference = value; }

        [JsonProperty("discount")]
        public decimal? Discount { get; set; }

        [JsonProperty("round_off")]
        public decimal? RoundOff { get; set; }

        [JsonProperty("roundOff")]
        private decimal? RoundOffCamel { set => RoundOff = value; }

        [JsonProperty("created_by")]
        public string CreatedBy { get; set; }

        [JsonProperty("createdBy")]
        private string CreatedByCamel { set => CreatedBy = value; }

        [JsonProperty("part")]
        public string Part { get; set; }

        [JsonProperty("items")]
        public List<CreateBillItemRequestDto> Items { get; set; } = new List<CreateBillItemRequestDto>();
    }
}
