using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class BillItemDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("bill_id")]
        public int BillId { get; set; }

        [JsonProperty("item_id")]
        public int? ItemId { get; set; }

        [JsonProperty("item_name")]
        public string ItemName { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }

        [JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("total_amount")]
        public decimal TotalAmount { get; set; }

        [JsonProperty("user_code")]
        public string UserCode { get; set; }

        [JsonProperty("tamil_name")]
        public string TamilName { get; set; }
    }
}
