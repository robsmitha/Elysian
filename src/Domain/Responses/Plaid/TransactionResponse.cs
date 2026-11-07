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
            /// <summary>On a posted transaction, the id of the pending transaction it replaces</summary>
            public string? pending_transaction_id { get; set; }
            public PersonalFinanceCategory? personal_finance_category { get; set; }
            public string? merchant_entity_id { get; set; }
            public object transaction_code { get; set; }
            public string transaction_id { get; set; }
            public string transaction_type { get; set; }
            public object unofficial_currency_code { get; set; }
        }

        public class PersonalFinanceCategory
        {
            public string? primary { get; set; }
            public string? detailed { get; set; }
            public string? confidence_level { get; set; }
        }
    }

    /// <summary>One page of /transactions/sync</summary>
    public class TransactionsSyncResponse
    {
        public List<Account> accounts { get; set; } = [];
        public List<TransactionResponse.Transaction> added { get; set; } = [];
        public List<TransactionResponse.Transaction> modified { get; set; } = [];
        public List<RemovedTransaction> removed { get; set; } = [];
        public string next_cursor { get; set; }
        public bool has_more { get; set; }
        public string request_id { get; set; }

        public class RemovedTransaction
        {
            public string transaction_id { get; set; }
            public string? account_id { get; set; }
        }
    }

    /// <summary>The public key Plaid signs webhooks with (/webhook_verification_key/get)</summary>
    public class WebhookVerificationKeyResponse
    {
        public WebhookVerificationKey key { get; set; }
        public string request_id { get; set; }
    }

    public class WebhookVerificationKey
    {
        public string alg { get; set; }
        public string crv { get; set; }
        public string kid { get; set; }
        public string kty { get; set; }
        public string use { get; set; }
        public string x { get; set; }
        public string y { get; set; }
        public long created_at { get; set; }
        public long? expired_at { get; set; }
    }
}
