using Elysian.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Merchants.Models
{
    public record SaveProductRequest(string Name, string Description, string SerialNumber,
            string Grade, List<SaveProductImage> AddImages, int? ProductId = null,
            string Code = "", string Sku = "", string LookupCode = "",
            int ProductTypeId = (int)ProductTypes.Trackables, int UnitTypeId = (int)UnitTypes.Quantity,
            int PriceTypeId = (int)PriceTypes.Fixed, decimal? Price = null, SaveProductSession? Session = null);
    public record SaveProductImage(string FileName, long FileSize, Guid StorageId);

    /// <summary>
    /// Session details, required when <see cref="SaveProductRequest.ProductTypeId"/> is <see cref="ProductTypes.Session"/>.
    /// The product's serial number is the session's URL slug.
    /// </summary>
    public record SaveProductSession(int DurationMinutes, string? Location, string Collection, List<string>? Features,
        string? PortfolioCategory, Guid? CoverPhotoId, int SortOrder, bool IsBookable);
}
