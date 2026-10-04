using Elysian.Application.Features.Booking.Models;
using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Booking
{
    /// <summary>
    /// Session products (product type <see cref="ProductTypes.Session"/> with their <see cref="ProductSession"/> details)
    /// </summary>
    public static class BookingSessions
    {
        public record SessionRow(Product Product, ProductSession Session, Photo? CoverPhoto);

        /// <summary>
        /// Every live session in display order, or just the one with <paramref name="slug"/>.
        /// Soft-deleted products drop out through the product's query filter.
        /// </summary>
        public static IQueryable<SessionRow> QuerySessions(this ElysianContext context, string? slug = null)
        {
            var products = context.Products.AsNoTracking().Where(p => p.ProductTypeId == (int)ProductTypes.Session);
            if (slug != null)
            {
                products = products.Where(p => p.SerialNumber == slug);
            }

            return from product in products
                   join session in context.ProductSessions.AsNoTracking() on product.ProductId equals session.ProductId
                   orderby session.SortOrder, product.Name
                   select new SessionRow(product, session,
                       session.CoverPhoto != null && session.CoverPhoto.Status == PhotoStatus.Ready ? session.CoverPhoto : null);
        }

        public static Task<SessionRow?> FindSessionAsync(this ElysianContext context, string slug, CancellationToken cancellationToken) =>
            context.QuerySessions(slug).FirstOrDefaultAsync(cancellationToken);

        public static bool IsPriceFrom(this Product product) => product.PriceTypeId == (int)PriceTypes.Variable;

        public static BookingSessionModel ToModel(this SessionRow row, IPhotoStorage photoStorage, string tenantIdentifier) =>
            new(row.Product.ProductId,
                row.Product.SerialNumber,
                row.Product.Name,
                row.Product.Description,
                row.Session.DurationMinutes,
                row.Product.Price,
                row.Product.IsPriceFrom(),
                row.Session.Location,
                row.Session.Collection,
                row.Session.Features,
                row.Session.PortfolioCategory,
                row.Session.IsBookable,
                row.Session.SortOrder,
                row.CoverPhoto?.ToPortfolioModel(photoStorage, tenantIdentifier, NoPlacements));

        private static readonly ILookup<Guid, string> NoPlacements = Array.Empty<(Guid, string)>().ToLookup(p => p.Item1, p => p.Item2);
    }
}
