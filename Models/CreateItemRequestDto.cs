using System.Collections.Generic;
using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class CreateItemRequestDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("category_id")]
        public int CategoryId { get; set; }

        [JsonProperty("units")]
        public string Units { get; set; }

        [JsonProperty("user_code")]
        public int? UserCode { get; set; }

        [JsonProperty("tamil_name")]
        public string TamilName { get; set; }

        [JsonProperty("is_kot")]
        public int? IsKot { get; set; }

        [JsonProperty("show_order")]
        public int? ShowOrder { get; set; }

        [JsonProperty("prices")]
        public List<ItemPriceInputDto> Prices { get; set; }

        [JsonProperty("price")]
        public decimal? Price { get; set; }

        [JsonProperty("table_price")]
        public decimal? TablePrice { get; set; }

        [JsonProperty("parcel_price")]
        public decimal? ParcelPrice { get; set; }

        [JsonProperty("online_price")]
        public decimal? OnlinePrice { get; set; }
    }

    public class ItemPriceInputDto
    {
        private int _locationId;

        [JsonProperty("location_id")]
        public int LocationId
        {
            get => _locationId;
            set => _locationId = value;
        }

        [JsonProperty("LocationId")]
        public int LocationIdPascal
        {
            set => _locationId = value;
        }

        private decimal _price;

        [JsonProperty("price")]
        public decimal Price
        {
            get => _price;
            set => _price = value;
        }

        [JsonProperty("Price")]
        public decimal PricePascal
        {
            set => _price = value;
        }
    }
}
