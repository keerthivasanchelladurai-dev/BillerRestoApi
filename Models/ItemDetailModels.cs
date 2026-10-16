using System.Collections.Generic;
using Newtonsoft.Json;

namespace BillerRestoApi.Models
{
    /// <summary>One location price (1 = TABLE, 2 = PARCEL, 8 = ONLINE).</summary>
    public class ItemDetailPriceDto
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

    /// <summary>Full item record returned by GET api/items/{id} (used to pre-fill the Edit form).</summary>
    public class ItemDetailDto
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        private string _name;

        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        [JsonProperty("Name")]
        public string NamePascal
        {
            set => _name = value;
        }

        private int? _categoryId;

        [JsonProperty("category_id")]
        public int? CategoryId
        {
            get => _categoryId;
            set => _categoryId = value;
        }

        [JsonProperty("CategoryId")]
        public int? CategoryIdPascal
        {
            set => _categoryId = value;
        }

        private string _units;

        [JsonProperty("units")]
        public string Units
        {
            get => _units;
            set => _units = value;
        }

        [JsonProperty("Units")]
        public string UnitsPascal
        {
            set => _units = value;
        }

        private string _tamilName;

        [JsonProperty("tamil_name")]
        public string TamilName
        {
            get => _tamilName;
            set => _tamilName = value;
        }

        [JsonProperty("TamilName")]
        public string TamilNamePascal
        {
            set => _tamilName = value;
        }

        private int? _userCode;

        [JsonProperty("user_code")]
        public int? UserCode
        {
            get => _userCode;
            set => _userCode = value;
        }

        [JsonProperty("UserCode")]
        public int? UserCodePascal
        {
            set => _userCode = value;
        }

        private int _isKot;

        [JsonProperty("is_kot")]
        public int IsKot
        {
            get => _isKot;
            set => _isKot = value;
        }

        [JsonProperty("IsKot")]
        public int IsKotPascal
        {
            set => _isKot = value;
        }

        private int _showOrder;

        [JsonProperty("show_order")]
        public int ShowOrder
        {
            get => _showOrder;
            set => _showOrder = value;
        }

        [JsonProperty("ShowOrder")]
        public int ShowOrderPascal
        {
            set => _showOrder = value;
        }

        private string _imagePath;

        [JsonProperty("image_path")]
        public string ImagePath
        {
            get => _imagePath;
            set => _imagePath = value;
        }

        [JsonProperty("ImagePath")]
        public string ImagePathPascal
        {
            set => _imagePath = value;
        }

        private List<ItemDetailPriceDto> _prices = new List<ItemDetailPriceDto>();

        [JsonProperty("prices")]
        public List<ItemDetailPriceDto> Prices
        {
            get => _prices;
            set => _prices = value;
        }

        [JsonProperty("Prices")]
        public List<ItemDetailPriceDto> PricesPascal
        {
            set => _prices = value;
        }
    }

    /// <summary>
    /// Body for PUT api/items/{id}. Superset of the old UpdateItemRequestDto:
    /// UserCode and Prices are optional (null = leave unchanged), so older clients keep working.
    /// </summary>
    public class UpdateItemDetailsRequestDto
    {
        private string _name;

        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        [JsonProperty("Name")]
        public string NamePascal
        {
            set => _name = value;
        }

        private int _categoryId;

        [JsonProperty("category_id")]
        public int CategoryId
        {
            get => _categoryId;
            set => _categoryId = value;
        }

        [JsonProperty("CategoryId")]
        public int CategoryIdPascal
        {
            set => _categoryId = value;
        }

        private string _units;

        [JsonProperty("units")]
        public string Units
        {
            get => _units;
            set => _units = value;
        }

        [JsonProperty("Units")]
        public string UnitsPascal
        {
            set => _units = value;
        }

        private string _tamilName;

        [JsonProperty("tamil_name")]
        public string TamilName
        {
            get => _tamilName;
            set => _tamilName = value;
        }

        [JsonProperty("TamilName")]
        public string TamilNamePascal
        {
            set => _tamilName = value;
        }

        private int _isKot = 1;

        [JsonProperty("is_kot")]
        public int IsKot
        {
            get => _isKot;
            set => _isKot = value;
        }

        [JsonProperty("IsKot")]
        public int IsKotPascal
        {
            set => _isKot = value;
        }

        private int _showOrder;

        [JsonProperty("show_order")]
        public int ShowOrder
        {
            get => _showOrder;
            set => _showOrder = value;
        }

        [JsonProperty("ShowOrder")]
        public int ShowOrderPascal
        {
            set => _showOrder = value;
        }

        private int? _userCode;

        [JsonProperty("user_code")]
        public int? UserCode
        {
            get => _userCode;
            set => _userCode = value;
        }

        [JsonProperty("UserCode")]
        public int? UserCodePascal
        {
            set => _userCode = value;
        }

        private List<ItemDetailPriceDto> _prices;

        [JsonProperty("prices")]
        public List<ItemDetailPriceDto> Prices
        {
            get => _prices;
            set => _prices = value;
        }

        [JsonProperty("Prices")]
        public List<ItemDetailPriceDto> PricesPascal
        {
            set => _prices = value;
        }
    }
}
