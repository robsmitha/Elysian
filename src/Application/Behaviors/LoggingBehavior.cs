using Elysian.Application.Interfaces;
using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Elysian.Application.Behaviors
{
    public class LoggingBehavior<TRequest>(
        ILogger<TRequest> logger, IClaimsPrincipalAccessor claimsPrincipalAccessor)
       : IRequestPreProcessor<TRequest>
        where TRequest : notnull
    {
        private readonly ILogger _logger = logger;

        public async Task Process(
            TRequest request,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            var user = claimsPrincipalAccessor.IsAuthenticated ? claimsPrincipalAccessor.UserId : "Anonymous";
            
            _logger.LogInformation($"Application Request [User: {user}, Claims: {claimsPrincipalAccessor.Claims}, RequestName: {requestName}, Payload: {request}]");
            
            await Task.FromResult(0);
        }
    }
}
