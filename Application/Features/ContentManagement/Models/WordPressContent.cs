using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.ContentManagement.Models
{
    public class WordPressContent
    {
        public dynamic Pages { get; set; }
        public dynamic Posts { get; set; }
        public dynamic Tags { get; set; }
        public dynamic Categories { get; set; }
    }
}
