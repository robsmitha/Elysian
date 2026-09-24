using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Photos.Commands
{
    /// <summary>
    /// Sets SortOrder to each photo's index in <see cref="PhotoIds"/>
    /// </summary>
    [Authorize(Policy = PolicyNames.PhotoWrite)]
    public record ReorderPhotosCommand(List<Guid> PhotoIds) : IRequest<bool>;

    public class ReorderPhotosCommandValidator : AbstractValidator<ReorderPhotosCommand>
    {
        public ReorderPhotosCommandValidator()
        {
            RuleFor(v => v.PhotoIds)
                .NotEmpty()
                .Must(ids => ids.Distinct().Count() == ids.Count)
                    .WithMessage("Each photo can only appear once.");
        }
    }

    public class ReorderPhotosCommandHandler(ElysianContext context) : IRequestHandler<ReorderPhotosCommand, bool>
    {
        public async Task<bool> Handle(ReorderPhotosCommand request, CancellationToken cancellationToken)
        {
            var photos = await context.Photos
                .Where(p => request.PhotoIds.Contains(p.PhotoId))
                .ToDictionaryAsync(p => p.PhotoId, cancellationToken);

            for (var i = 0; i < request.PhotoIds.Count; i++)
            {
                if (photos.TryGetValue(request.PhotoIds[i], out var photo))
                {
                    photo.SortOrder = i;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
