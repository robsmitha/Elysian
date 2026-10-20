using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Elysian.Application.Features.Placements;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// The placements container and publish queue clients, built once. In Azure both use the app's managed identity
    /// (its own DefaultAzureCredential, not any TokenCredential registered for other features); locally, connection
    /// strings for Azurite. Built lazily so an app with publishing configured off never touches storage.
    /// </summary>
    public sealed class PlacementStorageClients
    {
        private readonly PlacementPublishingSettings settings;
        private readonly IConfiguration configuration;
        private readonly Lazy<DefaultAzureCredential> credential = new(() => new DefaultAzureCredential());
        private readonly Lazy<BlobContainerClient> container;
        private readonly Lazy<QueueClient> queue;

        public PlacementStorageClients(PlacementPublishingSettings settings, IConfiguration configuration)
        {
            this.settings = settings;
            this.configuration = configuration;
            container = new(CreateContainer);
            queue = new(CreateQueue);
        }

        public BlobContainerClient Container => container.Value;

        public QueueClient Queue => queue.Value;

        private BlobContainerClient CreateContainer()
        {
            if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
            {
                var local = new BlobServiceClient(settings.ConnectionString).GetBlobContainerClient(settings.ContainerName);
                // Azurite only; in Azure the container is created once by an admin and the identity can't create it
                local.CreateIfNotExists(PublicAccessType.Blob);
                return local;
            }

            if (string.IsNullOrWhiteSpace(settings.BlobContainerUri))
            {
                throw new InvalidOperationException(
                    $"{PlacementPublishingSettings.SectionName}:BlobContainerUri (or ConnectionString locally) is not configured.");
            }

            return new BlobContainerClient(new Uri(settings.BlobContainerUri), credential.Value);
        }

        private QueueClient CreateQueue()
        {
            var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

            // The same settings the host's queue trigger binds to, so both always point at one queue
            var connectionString = configuration[PlacementQueue.ConnectionName];
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                var local = new QueueClient(connectionString, PlacementQueue.QueueName, options);
                local.CreateIfNotExists();
                return local;
            }

            var serviceUri = configuration[$"{PlacementQueue.ConnectionName}:queueServiceUri"];
            if (string.IsNullOrWhiteSpace(serviceUri))
            {
                throw new InvalidOperationException(
                    $"{PlacementQueue.ConnectionName}:queueServiceUri (or a {PlacementQueue.ConnectionName} connection string locally) is not configured.");
            }

            return new QueueClient(new Uri($"{serviceUri.TrimEnd('/')}/{PlacementQueue.QueueName}"), credential.Value, options);
        }
    }
}
