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
            int PriceTypeId = (int)PriceTypes.Fixed);
    public record SaveProductImage(string FileName, long FileSize, Guid StorageId);
}
