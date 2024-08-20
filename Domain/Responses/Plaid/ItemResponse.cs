using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Responses.Plaid
{
    public class ItemResponse
    {
        public Item item { get; set; }
        public string request_id { get; set; }
        public Status status { get; set; }

        public class Transactions
        {
            public DateTime? last_failed_update { get; set; }
            public DateTime? last_successful_update { get; set; }
        }

        public class Status
        {
            public object last_webhook { get; set; }
            public Transactions transactions { get; set; }
        }
    }
}
