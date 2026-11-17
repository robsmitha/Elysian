using Elysian.Application.Exceptions;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Platform.Commands
{
    /// <param name="Credential">The value to store as the edge client's secret. Shown once; only its hash is kept.</param>
    public record EdgeClientCredential(string Name, string Credential)
    {
        // Keeps the key out of request logging
        public override string ToString() => $"{nameof(EdgeClientCredential)} {{ Name = {Name} }}";
    }

    /// <summary>
    /// Registers something at the edge that may forward requests for a tenant, and returns its credential
    /// </summary>
    [RequiresPlatformAdmin]
    public record CreateEdgeClientCommand(string? Name, EdgeClientScope Scope, string? TenantIdentifier = null, string? Platform = null)
        : IRequest<EdgeClientCredential>;

    public class CreateEdgeClientCommandValidator : AbstractValidator<CreateEdgeClientCommand>
    {
        public CreateEdgeClientCommandValidator()
        {
            RuleFor(v => v.Name)
                .Must(EdgeClientKeys.IsValidName)
                .WithMessage("Use 3 to 64 lowercase letters, digits and dashes, starting and ending with a letter or digit.");

            RuleFor(v => v.Scope).IsInEnum().WithMessage("Choose what the client may act for.");

            // Separate rules: a chained When would apply to every check before it, not just the last
            When(v => v.Scope == EdgeClientScope.Tenant, () =>
                RuleFor(v => v.TenantIdentifier).NotEmpty().WithMessage("Choose the tenant this client acts for."))
            .Otherwise(() =>
                RuleFor(v => v.TenantIdentifier).Empty().WithMessage("Only a single-tenant client names a tenant."));

            When(v => v.Scope == EdgeClientScope.Platform, () =>
                RuleFor(v => v.Platform)
                    .Must(p => PlatformCatalog.Find(p) is { HasCloudRuntime: true })
                    .WithMessage("Choose a platform that has a shared template."))
            .Otherwise(() =>
                RuleFor(v => v.Platform).Empty().WithMessage("Only a platform client names a platform."));
        }
    }

    public class CreateEdgeClientCommandHandler(ElysianContext context) : IRequestHandler<CreateEdgeClientCommand, EdgeClientCredential>
    {
        public async Task<EdgeClientCredential> Handle(CreateEdgeClientCommand request, CancellationToken cancellationToken)
        {
            var name = request.Name!;
            if (await context.EdgeClients.AnyAsync(c => c.Name == name, cancellationToken))
            {
                throw new ConflictException($"An edge client named {name} already exists.");
            }

            var key = EdgeClientKeys.GenerateKey();
            context.EdgeClients.Add(new EdgeClient
            {
                Name = name,
                Scope = request.Scope,
                TenantIdentifier = request.Scope == EdgeClientScope.Tenant ? request.TenantIdentifier : null,
                Platform = request.Scope == EdgeClientScope.Platform ? PlatformCatalog.Find(request.Platform)!.Key : null,
                KeyHash = EdgeClientKeys.Hash(key),
                IsEnabled = true,
            });
            await context.SaveChangesAsync(cancellationToken);

            return new EdgeClientCredential(name, EdgeClientKeys.Format(name, key));
        }
    }

    /// <summary>
    /// Issues a new key. The old one keeps working until <see cref="RetirePreviousEdgeClientKeyCommand"/>,
    /// so the edge can be given the new credential first.
    /// </summary>
    [RequiresPlatformAdmin]
    public record RotateEdgeClientKeyCommand(string Name) : IRequest<EdgeClientCredential>;

    public class RotateEdgeClientKeyCommandHandler(ElysianContext context) : IRequestHandler<RotateEdgeClientKeyCommand, EdgeClientCredential>
    {
        public async Task<EdgeClientCredential> Handle(RotateEdgeClientKeyCommand request, CancellationToken cancellationToken)
        {
            var client = await context.EdgeClients.FirstOrDefaultAsync(c => c.Name == request.Name, cancellationToken)
                ?? throw new NotFoundException(nameof(EdgeClient), request.Name);

            var key = EdgeClientKeys.GenerateKey();
            client.PreviousKeyHash = client.KeyHash;
            client.KeyHash = EdgeClientKeys.Hash(key);
            await context.SaveChangesAsync(cancellationToken);

            return new EdgeClientCredential(client.Name, EdgeClientKeys.Format(client.Name, key));
        }
    }

    /// <summary>
    /// Stops accepting the key from before the last rotation. Hosts may go on accepting it from memory for up
    /// to Edge:CacheSeconds.
    /// </summary>
    [RequiresPlatformAdmin]
    public record RetirePreviousEdgeClientKeyCommand(string Name) : IRequest;

    public class RetirePreviousEdgeClientKeyCommandHandler(ElysianContext context) : IRequestHandler<RetirePreviousEdgeClientKeyCommand>
    {
        public async Task Handle(RetirePreviousEdgeClientKeyCommand request, CancellationToken cancellationToken)
        {
            var client = await context.EdgeClients.FirstOrDefaultAsync(c => c.Name == request.Name, cancellationToken)
                ?? throw new NotFoundException(nameof(EdgeClient), request.Name);

            client.PreviousKeyHash = null;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
