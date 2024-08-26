using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Infrastructure.Settings
{
    public class GitHubSettings
    {
        public string ClientId { get; set; }
        public string Secret { get; set; }
        public string StateSecretKey { get; set; }
        public string DefaultAccessToken { get; set; }
        public string UserAgent { get; set; }
        public Uri SuccessRedirectUri { get; set; }
    }
}
