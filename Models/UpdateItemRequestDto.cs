using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;


namespace BillerRestoApi.Models
{
    public class UpdateItemRequestDto
    {
        public string Name { get; set; }
        public int CategoryId { get; set; }
        public string Units { get; set; }
        public string TamilName { get; set; }
        public int IsKot { get; set; }
        public int ShowOrder { get; set; }
    }
}
