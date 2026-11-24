using Elysian.Application.Exceptions;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Photos
{
    /// <param name="UsedBytes">The uploaded files of every photo the site has</param>
    /// <param name="LimitBytes">The most it may hold, or null when it has no limit</param>
    public sealed record StorageUsageModel(long UsedBytes, long? LimitBytes, int Photos);

    /// <summary>
    /// How much of its storage the current site has used. What counts is the files that were uploaded, at the
    /// size they were uploaded: the copies made from them for the web are small beside them and aren't
    /// counted. Only a site drawn with a shared template has a limit; a site with its own code has none.
    /// </summary>
    public class StorageQuota(ElysianContext context, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        IOptions<PlatformStorageSettings>? settings = null)
    {
        public const long Gigabyte = 1024L * 1024 * 1024;

        /// <summary>The most a site of its own limit may be given, in gigabytes</summary>
        public const int MostGb = 2000;

        public long? LimitBytes()
        {
            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;
            if (tenant.Runtime != RuntimeNames.Cloud)
            {
                return null;
            }
            var gigabytes = tenant.StorageQuotaGb ?? (settings?.Value ?? new PlatformStorageSettings()).DefaultQuotaGb;
            return gigabytes > 0 ? gigabytes * Gigabyte : null;
        }

        public async Task<StorageUsageModel> UsageAsync(CancellationToken cancellationToken)
        {
            var used = await context.Photos.SumAsync(p => (long?)p.OriginalFileSize, cancellationToken) ?? 0;
            var photos = await context.Photos.CountAsync(cancellationToken);
            return new StorageUsageModel(used, LimitBytes(), photos);
        }

        /// <exception cref="ConflictException">The upload would take the site over its limit</exception>
        public async Task EnsureRoomAsync(long bytes, CancellationToken cancellationToken)
        {
            if (LimitBytes() is not { } limit)
            {
                return;
            }
            var used = await context.Photos.SumAsync(p => (long?)p.OriginalFileSize, cancellationToken) ?? 0;
            if (used + bytes > limit)
            {
                throw new ConflictException(
                    $"This site has used {Shown(used)} of its {Shown(limit)} of photo storage, so this photo doesn't fit. Delete photos you no longer need, or ask for more space.");
            }
        }

        /// <summary>"24.6 GB", "310 MB"</summary>
        public static string Shown(long bytes) => bytes >= Gigabyte
            ? $"{Math.Round(bytes / (double)Gigabyte, 1):0.#} GB"
            : $"{Math.Max(0, bytes / (1024 * 1024))} MB";
    }

    [Authorize(Policy = PolicyNames.PhotoRead)]
    public record GetStorageUsageQuery : IRequest<StorageUsageModel>;

    public class GetStorageUsageQueryHandler(StorageQuota quota) : IRequestHandler<GetStorageUsageQuery, StorageUsageModel>
    {
        public Task<StorageUsageModel> Handle(GetStorageUsageQuery request, CancellationToken cancellationToken) => quota.UsageAsync(cancellationToken);
    }
}

namespace Elysian.Application.Features.Platform.Commands
{
    using Elysian.Application.Features.Photos;

    /// <summary>
    /// Gives a site a storage limit of its own, or (with no number) puts it back on the platform's.
    /// </summary>
    /// <param name="QuotaGb">Gigabytes of uploaded photos, or null for the platform's default</param>
    [RequiresPlatformAdmin]
    public record SetTenantStorageQuotaCommand(string? Identifier, int? QuotaGb) : IRequest;

    public class SetTenantStorageQuotaCommandValidator : AbstractValidator<SetTenantStorageQuotaCommand>
    {
        public SetTenantStorageQuotaCommandValidator()
        {
            RuleFor(v => v.Identifier).NotEmpty().WithMessage("Choose a site.");
            RuleFor(v => v.QuotaGb)
                .InclusiveBetween(1, StorageQuota.MostGb)
                .When(v => v.QuotaGb != null)
                .WithMessage($"Enter a whole number of gigabytes from 1 to {StorageQuota.MostGb}, or leave it empty for the usual limit.");
        }
    }

    public class SetTenantStorageQuotaCommandHandler(PlatformContext platform) : IRequestHandler<SetTenantStorageQuotaCommand>
    {
        public async Task Handle(SetTenantStorageQuotaCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var tenant = await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken)
                ?? throw new NotFoundException("Site", identifier);

            tenant.StorageQuotaGb = request.QuotaGb;
            await platform.SaveChangesAsync(cancellationToken);
        }
    }
}
