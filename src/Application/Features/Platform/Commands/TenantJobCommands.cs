using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Platform.Commands
{
    public record EnqueueTenantJobsResult(int Queued, int Failed);

    /// <summary>
    /// Queues a scheduled job once for each tenant that should run it. Called by a timer, outside any tenant;
    /// which tenants have the module is read across tenants through <see cref="PlatformContext"/>.
    /// </summary>
    public record EnqueueTenantJobsCommand(string Job) : IRequest<EnqueueTenantJobsResult>;

    public class EnqueueTenantJobsCommandHandler(PlatformContext platform, IMultiTenantStore<ElysianTenantInfo> tenantStore,
        ITenantJobQueue queue, ILogger<EnqueueTenantJobsCommandHandler> logger) : IRequestHandler<EnqueueTenantJobsCommand, EnqueueTenantJobsResult>
    {
        public async Task<EnqueueTenantJobsResult> Handle(EnqueueTenantJobsCommand request, CancellationToken cancellationToken)
        {
            var job = TenantJobCatalog.Find(request.Job) ?? throw new NotFoundException("Tenant job", request.Job);

            var tenants = (await tenantStore.GetAllAsync())
                .Where(t => !string.IsNullOrEmpty(t.Id) && !string.IsNullOrEmpty(t.Identifier))
                .Where(t => !string.Equals(t.Status, TenantStatusNames.Suspended, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (job.Module != null)
            {
                var withModule = (await platform.TenantModules.AsNoTracking()
                    .Where(m => m.ModuleKey == job.Module && (m.Status == TenantModuleStatus.Active || m.Status == TenantModuleStatus.Trial))
                    .Select(m => m.TenantId)
                    .ToListAsync(cancellationToken)).ToHashSet();

                tenants = tenants.Where(t => withModule.Contains(t.Id!)).ToList();
            }

            var queued = 0;
            var failed = 0;
            foreach (var tenant in tenants)
            {
                try
                {
                    await queue.EnqueueAsync(new TenantJobMessage(tenant.Identifier!, job.Key), cancellationToken);
                    queued++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // Keep going: one tenant's message failing mustn't cost the others their run
                    failed++;
                    logger.LogError(ex, "Couldn't queue {Job} for {Tenant}", job.Key, tenant.Identifier);
                }
            }

            logger.LogInformation("Queued {Job} for {Queued} tenant(s), {Failed} failed", job.Key, queued, failed);
            return new EnqueueTenantJobsResult(queued, failed);
        }
    }

    /// <summary>
    /// Runs one queued job inside the tenant it was queued for. The work itself is the job's own request, so
    /// its module check and validation apply as they would for any other caller.
    /// </summary>
    public record RunTenantJobCommand(string Job) : IRequest<string>;

    public class RunTenantJobCommandHandler(ISender sender, ILogger<RunTenantJobCommandHandler> logger) : IRequestHandler<RunTenantJobCommand, string>
    {
        public const string Skipped = "Skipped: the module is no longer enabled for this tenant";

        public async Task<string> Handle(RunTenantJobCommand request, CancellationToken cancellationToken)
        {
            var job = TenantJobCatalog.Find(request.Job) ?? throw new NotFoundException("Tenant job", request.Job);

            try
            {
                var result = await sender.Send((object)job.CreateRequest(), cancellationToken);
                return result?.ToString() ?? "Done";
            }
            catch (ModuleNotEnabledException)
            {
                // Switched off between being queued and being run. Not an error, and not worth a retry.
                logger.LogInformation("Skipped {Job}: the tenant no longer has the module", job.Key);
                return Skipped;
            }
        }
    }
}
