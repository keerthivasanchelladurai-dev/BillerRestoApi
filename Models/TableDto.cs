using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class TableDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("location_id")]
        public int? LocationId { get; set; }
    }
}
