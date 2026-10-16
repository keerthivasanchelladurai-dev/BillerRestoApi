using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class ItemDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("category_id")]
        public int? CategoryId { get; set; }

        [JsonProperty("location_id")]
        public int LocationId { get; set; }

        [JsonProperty("price")]
        public decimal Price { get; set; }

        [JsonProperty("user_code")]
        public int? UserCode { get; set; }

        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("is_kot")]
        public int IsKot { get; set; }

        [JsonProperty("show_order")]
        public int ShowOrder { get; set; }

        [JsonProperty("image_path")]
        public string ImagePath { get; set; }




    }
}
