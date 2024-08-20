using MediatR.Pipeline;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Elysian.Application.Behaviors
{
    public class LoggingBehavior<TRequest>(
        ILogger<TRequest> logger)
       : IRequestPreProcessor<TRequest>
        where TRequest : notnull
    {
        private readonly ILogger _logger = logger;

        public async Task Process(
            TRequest request,
            CancellationToken cancellationToken)
        {
            var requestName = typeof(TRequest).Name;
            _logger.LogInformation($"Application Request: {requestName} {request}");
            await Task.FromResult(0);
        }
    }
}
