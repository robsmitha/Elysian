using Azure.Identity;
using Azure.Storage.Queues;
using Elysian.Application.Features.Platform;
using Elysian.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Sends tenant jobs to the Function App's own storage queue (the AzureWebJobsStorage setting the queue
    /// trigger reads): a connection string locally (Azurite) or when set, otherwise the app's managed identity
    /// with AzureWebJobsStorage__queueServiceUri or __accountName. Messages are base64, as the trigger expects.
    /// </summary>
    public sealed class AzureTenantJobQueue(IConfiguration configuration) : ITenantJobQueue
    {
        public const string ConnectionName = "AzureWebJobsStorage";

        private readonly SemaphoreSlim gate = new(1, 1);
        private QueueClient? client;

        public async Task EnqueueAsync(TenantJobMessage message, CancellationToken cancellationToken = default)
        {
            var queue = await GetClientAsync(cancellationToken);
            await queue.SendMessageAsync(message.Serialize(), cancellationToken);
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
                return client ??= await CreateAsync(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<QueueClient> CreateAsync(CancellationToken cancellationToken)
        {
            var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

            var connectionString = configuration[ConnectionName];
            QueueClient created;
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                created = new QueueClient(connectionString, TenantJobMessage.QueueName, options);
            }
            else
            {
                var serviceUri = configuration[$"{ConnectionName}:queueServiceUri"];
                var accountName = configuration[$"{ConnectionName}:accountName"];
                var baseUri = !string.IsNullOrWhiteSpace(serviceUri) ? serviceUri.TrimEnd('/')
                    : !string.IsNullOrWhiteSpace(accountName) ? $"https://{accountName}.queue.core.windows.net"
                    : throw new InvalidOperationException($"{ConnectionName} (or {ConnectionName}__queueServiceUri / __accountName) is not configured.");

                created = new QueueClient(new Uri($"{baseUri}/{TenantJobMessage.QueueName}"), new DefaultAzureCredential(), options);
            }

            await created.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            return created;
        }
    }
}
