using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    public class CreateCategoryRequestDto
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("show_order")]
        public int? ShowOrder { get; set; }

        [JsonProperty("created_by")]
        public string CreatedBy { get; set; }
    }
}
