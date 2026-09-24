using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Responses.Plaid
{
    public class Balances
    {
        public double? available { get; set; }
        public double current { get; set; }
        public string iso_currency_code { get; set; }
        public double? limit { get; set; }
        public object unofficial_currency_code { get; set; }
    }
}
