using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Responses.Plaid
{
    public class Item
    {
        public List<string> available_products { get; set; }
        public List<string> billed_products { get; set; }
        public object consent_expiration_time { get; set; }
        public Error error { get; set; }
        public string institution_id { get; set; }
        public string item_id { get; set; }
        public List<string> products { get; set; }
        public string update_type { get; set; }
        public string webhook { get; set; }
    }
}
