using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Responses.Plaid
{
    public class InstitutionResponse
    {
        public Institution institution { get; set; }
        public string request_id { get; set; }

        public class Institution
        {
            public List<string> country_codes { get; set; }
            public string institution_id { get; set; }
            public string name { get; set; }
            public bool oauth { get; set; }
            public List<string> products { get; set; }
            public List<object> routing_numbers { get; set; }
        }
    }
}
