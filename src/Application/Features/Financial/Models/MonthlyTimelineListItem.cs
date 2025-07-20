using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.Financial.Models
{
    public record MonthlyTimelineListItem(string Text, int Month, int Year)
    {
        public DateTime StartOfMonth => new (Year, Month, 1);
        public DateTime EndOfMonth => new DateTime(Year, Month, DateTime.DaysInMonth(Year, Month)).AddDays(1).AddTicks(-1);
    }
}
