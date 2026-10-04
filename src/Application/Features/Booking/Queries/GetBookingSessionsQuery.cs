using Elysian.Application.Exceptions;
using Elysian.Application.Features.Booking.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Booking.Queries
{
    /// <summary>
    /// Public, anonymous list of every session product (bookable or not) in display order
    /// </summary>
    public record GetBookingSessionsQuery : IRequest<List<BookingSessionModel>>;

    public class GetBookingSessionsQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetBookingSessionsQuery, List<BookingSessionModel>>
    {
        public async Task<List<BookingSessionModel>> Handle(GetBookingSessionsQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var rows = await context.QuerySessions().ToListAsync(cancellationToken);
            return rows.Select(r => r.ToModel(photoStorage, tenantIdentifier)).ToList();
        }
    }

    /// <summary>
    /// Public, anonymous read of one session by its slug; 404 when there's no such session
    /// </summary>
    public record GetBookingSessionQuery(string Slug) : IRequest<BookingSessionModel>;

    public class GetBookingSessionQueryHandler(ElysianContext context, IPhotoStorage photoStorage,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
        : IRequestHandler<GetBookingSessionQuery, BookingSessionModel>
    {
        public async Task<BookingSessionModel> Handle(GetBookingSessionQuery request, CancellationToken cancellationToken)
        {
            var tenantIdentifier = multiTenantContextAccessor.MultiTenantContext.TenantInfo!.Identifier!;
            var row = await context.FindSessionAsync(request.Slug, cancellationToken) ?? throw new NotFoundException();
            return row.ToModel(photoStorage, tenantIdentifier);
        }
    }
}
