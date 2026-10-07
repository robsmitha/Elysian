using Elysian.Application.Features.UserManagement.Models;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.UserManagement.Commands
{
    /// <summary>
    /// Changes a managed user's display name, here and (for guests) in Entra ID
    /// </summary>
    [Authorize(Policy = PolicyNames.UserWrite)]
    public record UpdateManagedUserCommand(int Id, string? DisplayName) : IRequest<ManagedUserModel>;

    public class UpdateManagedUserCommandValidator : AbstractValidator<UpdateManagedUserCommand>
    {
        public UpdateManagedUserCommandValidator()
        {
            RuleFor(v => v.Id).GreaterThan(0);

            RuleFor(v => v.DisplayName)
                .NotEmpty().WithMessage("Please enter a display name.")
                .MaximumLength(256).WithMessage("Display name must be 256 characters or fewer.");
        }
    }

    public class UpdateManagedUserCommandHandler(UserManagementService service) : IRequestHandler<UpdateManagedUserCommand, ManagedUserModel>
    {
        public Task<ManagedUserModel> Handle(UpdateManagedUserCommand request, CancellationToken cancellationToken) =>
            service.UpdateAsync(request.Id, request.DisplayName!, cancellationToken);
    }
}
