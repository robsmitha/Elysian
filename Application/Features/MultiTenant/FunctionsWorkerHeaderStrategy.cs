using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.MultiTenant
{
    public class FunctionsWorkerHeaderStrategy(string headerKey) : IMultiTenantStrategy
    {
        public Task<string?> GetIdentifierAsync(object context)
        {
            if (context is not HttpRequestData httpRequestData)
            {
                throw new MultiTenantException(null,
                    new ArgumentException($"\"{nameof(context)}\" type must be of type HttpRequestData", nameof(context)));
            }

            if (!httpRequestData.Headers.TryGetValues(headerKey, out var values) || values?.Any() != true)
            {
                throw new MultiTenantException(null,
                    new ArgumentException($"\"{headerKey}\" header not found in HttpRequestData", nameof(context)));
            }

            return Task.FromResult(values.FirstOrDefault());
        }
    }
}
