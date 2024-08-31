using Elysian.Application.Interfaces;
using MediatR;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Elysian.Application.Behaviors
{
    public class LoggingBehavior<TRequest, TResponse>(
        ILogger<TRequest> logger, IClaimsPrincipalAccessor claimsPrincipalAccessor)
         : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly ILogger _logger = logger;

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            var user = claimsPrincipalAccessor.IsAuthenticated ? claimsPrincipalAccessor.UserId : "Anonymous";
            
            _logger.LogInformation($"Application Request [User: {user}, Claims: {claimsPrincipalAccessor.Claims}, RequestName: {requestName}, Payload: {request}]");

            return await next();
        }
    }
}
