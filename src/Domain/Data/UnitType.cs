using Elysian.Domain.Seedwork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Data
{
    public class UnitType : AuditableEntity
    {
        public int UnitTypeId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// Abbreviation (i.e. 4 each)
        /// </summary>
        public string PerUnit { get; set; }

        public class Configuration : AuditableEntityConfiguration<UnitType>
        {
            public override void Configure(EntityTypeBuilder<UnitType> builder)
            {
                base.Configure(builder);

                builder.HasKey(k => k.UnitTypeId);
                builder.Property(e => e.Name).IsRequired();
                builder.HasIndex(e => e.Name).IsUnique().HasDatabaseName("AK_UnitType_Name");

                builder.ToTable("UnitType");
            }
        }
    }
}
