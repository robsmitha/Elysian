using Elysian.Application.Features.MultiTenant;
using Finbuckle.MultiTenant.EntityFrameworkCore.Stores.EFCoreStore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Infrastructure.Context
{
    public partial class TenantContext(DbContextOptions<TenantContext> options) 
        : EFCoreStoreDbContext<ElysianTenantInfo>(options)
    {
    }
}
