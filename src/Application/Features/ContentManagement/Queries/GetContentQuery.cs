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
    public record GetContentQuery : IRequest<WordPressContent>;

    public class GetContentQueryHandler(IWordPressService wordPressService) : IRequestHandler<GetContentQuery, WordPressContent>
    {
        public async Task<WordPressContent> Handle(GetContentQuery request, CancellationToken cancellationToken)
        {
            return await wordPressService.GetWordPressContentAsync();
        }
    }
}
