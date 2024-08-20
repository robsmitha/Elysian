using Elysian.Application.Features.ContentManagement.Models;
using Elysian.Application.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Features.ContentManagement.Queries
{
    public class GetContentQuery : IRequest<WordPressContent>
    {
        public class Handler(IWordPressService wordPressService) : IRequestHandler<GetContentQuery, WordPressContent>
        {
            public async Task<WordPressContent> Handle(GetContentQuery request, CancellationToken cancellationToken)
            {
                return await wordPressService.GetWordPressContentAsync();
            }
        }
    }
}
