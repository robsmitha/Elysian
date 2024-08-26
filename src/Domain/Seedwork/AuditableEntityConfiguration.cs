using Elysian.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Seedwork
{
    public abstract class AuditableEntityConfiguration<T> : IEntityTypeConfiguration<T> 
        where T : class, IAuditableEntitiy
    {
        public virtual void Configure(EntityTypeBuilder<T> builder)
        {
            builder.Property(e => e.CreatedByUserId).IsRequired();
            builder.Property(e => e.ModifiedByUserId).IsRequired();
        }
    }
}
