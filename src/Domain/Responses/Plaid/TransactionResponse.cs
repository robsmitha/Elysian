using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Responses.Plaid
{
    public class TransactionResponse
    {
        public List<Account> accounts { get; set; }
        public Item item { get; set; }
        public string request_id { get; set; }
        public int total_transactions { get; set; }
        public List<Transaction> transactions { get; set; }

        public class Location
        {
            public object address { get; set; }
            public object city { get; set; }
            public object country { get; set; }
            public object lat { get; set; }
            public object lon { get; set; }
            public object postal_code { get; set; }
            public object region { get; set; }
            public string store_number { get; set; }
        }

        public class PaymentMeta
        {
            public object by_order_of { get; set; }
            public object payee { get; set; }
            public object payer { get; set; }
            public string payment_method { get; set; }
            public object payment_processor { get; set; }
            public object ppd_id { get; set; }
            public object reason { get; set; }
            public object reference_number { get; set; }
        }

        public class Transaction
        {
            public string account_id { get; set; }
            public object account_owner { get; set; }
            public double amount { get; set; }
            public string authorized_date { get; set; }
            public object authorized_datetime { get; set; }
            public List<string> category { get; set; }
            public string category_id { get; set; }
            public object check_number { get; set; }
            public string date { get; set; }
            public object datetime { get; set; }
            public string iso_currency_code { get; set; }
            public Location location { get; set; }
            public string merchant_name { get; set; }
            public string name { get; set; }
            public string payment_channel { get; set; }
            public PaymentMeta payment_meta { get; set; }
            public bool pending { get; set; }
            public object pending_transaction_id { get; set; }
            public object personal_finance_category { get; set; }
            public object transaction_code { get; set; }
            public string transaction_id { get; set; }
            public string transaction_type { get; set; }
            public object unofficial_currency_code { get; set; }
        }

    }
}
