using System.Collections.Generic;
using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class CreateItemResponseDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("category_id")]
        public int? CategoryId { get; set; }

        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("user_code")]
        public int? UserCode { get; set; }

        [JsonProperty("tamil_name")]
        public string TamilName { get; set; }

        [JsonProperty("is_kot")]
        public int IsKot { get; set; }

        [JsonProperty("show_order")]
        public int ShowOrder { get; set; }

        [JsonProperty("prices")]
        public List<ItemPriceDto> Prices { get; set; }
    }

    public class ItemPriceDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("item_id")]
        public int ItemId { get; set; }

        [JsonProperty("location_id")]
        public int LocationId { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }
    }
}
