using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class LocationDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }
}
