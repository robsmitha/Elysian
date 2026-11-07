using Azure.Identity;
using Azure.Storage.Queues;
using Elysian.Application.Features.Financial.Sync;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Sends sync jobs to the Function App's own storage queue (the same AzureWebJobsStorage setting the queue trigger
    /// reads): a connection string locally (Azurite) or when set, otherwise the app's managed identity with
    /// AzureWebJobsStorage__queueServiceUri or __accountName. Messages are base64, as the Functions trigger expects.
    /// </summary>
    public sealed class AzurePlaidSyncQueue : IPlaidSyncQueue
    {
        public const string ConnectionName = "AzureWebJobsStorage";

        private readonly IConfiguration configuration;
        private readonly string queueName;
        private readonly IMultiTenantContextAccessor<ElysianTenantInfo> tenantAccessor;
        private readonly SemaphoreSlim gate = new(1, 1);
        private QueueClient? client;

        public AzurePlaidSyncQueue(IConfiguration configuration, IOptions<PlaidSettings> settings, IMultiTenantContextAccessor<ElysianTenantInfo> tenantAccessor)
        {
            this.configuration = configuration;
            this.tenantAccessor = tenantAccessor;
            queueName = settings.Value.SyncQueueName;
        }

        public async Task EnqueueAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken = default)
        {
            var tenant = tenantAccessor.MultiTenantContext?.TenantInfo?.Identifier
                ?? throw new InvalidOperationException("A sync can only be queued inside a tenant.");
            var queue = await GetClientAsync(cancellationToken);
            await queue.SendMessageAsync(PlaidSyncQueue.Serialize(new PlaidSyncMessage(tenant, institutionAccessItemId, trigger)), cancellationToken);
        }

        // Built on first use; a failure (storage briefly unreachable) is retried on the next call rather than cached
        private async Task<QueueClient> GetClientAsync(CancellationToken cancellationToken)
        {
            if (client != null)
            {
                return client;
            }
            await gate.WaitAsync(cancellationToken);
            try
            {
                return client ??= await CreateAsync(configuration, queueName);
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<QueueClient> CreateAsync(IConfiguration configuration, string queueName)
        {
            var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

            var connectionString = configuration[ConnectionName];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                var client = new QueueClient(connectionString, queueName, options);
                await client.CreateIfNotExistsAsync();
                return client;
            }

            var serviceUri = configuration[$"{ConnectionName}:queueServiceUri"];
            var accountName = configuration[$"{ConnectionName}:accountName"];
            var baseUri = !string.IsNullOrWhiteSpace(serviceUri) ? serviceUri.TrimEnd('/')
                : !string.IsNullOrWhiteSpace(accountName) ? $"https://{accountName}.queue.core.windows.net"
                : throw new InvalidOperationException($"{ConnectionName} (or {ConnectionName}__queueServiceUri / __accountName) is not configured.");

            var identityClient = new QueueClient(new Uri($"{baseUri}/{queueName}"), new DefaultAzureCredential(), options);
            await identityClient.CreateIfNotExistsAsync();
            return identityClient;
        }
    }
}
