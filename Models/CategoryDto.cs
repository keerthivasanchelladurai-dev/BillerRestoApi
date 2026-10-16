using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class CategoryDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("show_order")]
        public int ShowOrder { get; set; }
    }
}
