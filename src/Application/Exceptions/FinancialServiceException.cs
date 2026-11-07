using Elysian.Domain.Responses.Plaid;
using System;

namespace Elysian.Application.Exceptions
{
    public class FinancialServiceException : Exception
    {
        public Error Error { get; set; }

        public FinancialServiceException(string message) : base(message)
        {
            Error = new Error { error_message = message };
        }

        // Plaid can answer with a body that isn't its JSON error (a gateway page, an empty body), so the error may be null.
        public FinancialServiceException(Error? error, string? fallbackMessage = null)
            : base(error?.error_message ?? fallbackMessage ?? "The financial service returned an error.")
        {
            Error = error ?? new Error { error_message = Message };
        }
    }
}
