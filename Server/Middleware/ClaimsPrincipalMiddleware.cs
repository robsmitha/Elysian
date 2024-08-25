using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Elysian.Application.Interfaces;
using System.Text.Json;
using System.Text;

namespace Elysian.Middleware
{
    public class ClaimsPrincipalMiddleware : IFunctionsWorkerMiddleware
    {
        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            // determine the type, the default is Microsoft.Azure.Functions.Worker.Context.Features.GrpcFunctionBindingsFeature
            (Type featureType, object featureInstance) = context.Features.SingleOrDefault(x => x.Key.Name == "IFunctionBindingsFeature");

            // find the input binding of the function which has been invoked and then find the associated parameter of the function for the data we want
            var inputData = featureType.GetProperties().SingleOrDefault(p => p.Name == "InputData")?.GetValue(featureInstance) as IReadOnlyDictionary<string, object>;
            var requestData = inputData?.Values.SingleOrDefault(obj => obj is HttpRequestData) as HttpRequestData;

            if (requestData?.ParsePrincipal() is ClaimsPrincipal principal)
            {
                // set the principal on the accessor from DI
                var accessor = context.InstanceServices.GetRequiredService<IClaimsPrincipalAccessor>();
                accessor.Principal = principal;
            }

            await next(context);
        }
    }

    public static class ClaimsPrincipalProcessing
    {
        private class ClientPrincipal
        {
            public string IdentityProvider { get; set; }

            public string UserId { get; set; }

            public string UserDetails { get; set; }

            public IEnumerable<string>? UserRoles { get; set; }
        }

        /// <summary>
        /// Code below originally from Microsoft Docs - https://docs.microsoft.com/en-gb/azure/static-web-apps/user-information?tabs=csharp#api-functions
        /// </summary>
        /// <param name="req">The HttpRequestData header.</param>
        /// <returns>Parsed ClaimsPrincipal from 'x-ms-client-principal' header.</returns>
        public static ClaimsPrincipal ParsePrincipal(this HttpRequestData req)
        {
            var principal = new ClientPrincipal();

            if (req.Headers.TryGetValues("x-ms-client-principal", out var header))
            {
                var data = header.First();
                var decoded = Convert.FromBase64String(data);
                var json = Encoding.UTF8.GetString(decoded);
                principal = JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }

            principal.UserRoles = principal.UserRoles?.Except(new[] { "anonymous" }, StringComparer.CurrentCultureIgnoreCase);

            if (!principal.UserRoles?.Any() ?? true)
            {
                return new ClaimsPrincipal();
            }

            var identity = new ClaimsIdentity(principal.IdentityProvider);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.UserId));
            identity.AddClaim(new Claim(ClaimTypes.Name, principal.UserDetails));
            identity.AddClaims(principal.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

            return new ClaimsPrincipal(identity);
        }
    }
}
