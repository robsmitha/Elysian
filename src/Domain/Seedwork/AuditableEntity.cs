using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Seedwork
{
    public abstract class AuditableEntity
    {
        public string CreatedByUserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string ModifiedByUserId { get; set; }
        public DateTimeOffset ModifiedAt { get; set; }
        public bool IsDeleted { get; set; }
    }
}
