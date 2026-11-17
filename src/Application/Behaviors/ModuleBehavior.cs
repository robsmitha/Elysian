using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Behaviors
{
    /// <summary>
    /// Rejects a request whose module the tenant doesn't have, before authorization, the handler or any
    /// external call runs.
    /// </summary>
    public class ModuleBehavior<TRequest, TResponse>(ITenantModuleService tenantModuleService) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var module = RequestModules.For(request.GetType());
            if (module != null && !await tenantModuleService.IsEnabledAsync(module, cancellationToken))
            {
                throw new ModuleNotEnabledException(module);
            }

            return await next();
        }
    }
}
