using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Domain.Constants
{
    public enum ProductTypes
    {
        Trackables = 1,

        /// <summary>
        /// Bookable photo sessions; details live in <see cref="Data.ProductSession"/>
        /// </summary>
        Session = 2,
    }
}
