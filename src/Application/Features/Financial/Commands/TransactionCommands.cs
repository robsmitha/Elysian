using System.Text.Json.Serialization;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>The Transactions page: search with filters, counts per filter, and the counted total.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record SearchTransactionsQuery(TransactionSearch Search) : IRequest<TransactionSearchResult>;

    public class SearchTransactionsQueryHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<SearchTransactionsQuery, TransactionSearchResult>
    {
        public Task<TransactionSearchResult> Handle(SearchTransactionsQuery request, CancellationToken cancellationToken) =>
            transactions.SearchAsync(principal.UserId!, request.Search, cancellationToken);
    }

    /// <summary>Category, treatment, notes, splits, and optionally "always do this" for one transaction.</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record UpdateTransactionCommand(TransactionUpdate Update) : IRequest<TransactionModel>;

    public class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
    {
        public UpdateTransactionCommandValidator()
        {
            RuleFor(c => c.Update).NotNull();
            RuleFor(c => c.Update.Id).GreaterThan(0).When(c => c.Update != null);
            RuleFor(c => c.Update.Notes).MaximumLength(1000).When(c => c.Update != null);
            RuleFor(c => c.Update.Splits).Must(s => s == null || s.Count <= 20).WithMessage("A transaction can be split into at most 20 lines.").When(c => c.Update != null);
            RuleFor(c => c.Update.CreateRule!.Value).MaximumLength(200).When(c => c.Update?.CreateRule != null);
        }
    }

    public class UpdateTransactionCommandHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<UpdateTransactionCommand, TransactionModel>
    {
        public Task<TransactionModel> Handle(UpdateTransactionCommand request, CancellationToken cancellationToken) =>
            transactions.UpdateAsync(principal.UserId!, request.Update, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record BulkUpdateTransactionsCommand(List<long> Ids, int? CategoryId,
        [property: JsonConverter(typeof(JsonStringEnumConverter))] Treatment? Treatment) : IRequest<int>;

    public class BulkUpdateTransactionsCommandValidator : AbstractValidator<BulkUpdateTransactionsCommand>
    {
        public BulkUpdateTransactionsCommandValidator()
        {
            RuleFor(c => c.Ids).NotEmpty().Must(ids => ids.Count <= 500).WithMessage("At most 500 transactions at a time.");
            RuleFor(c => c).Must(c => c.CategoryId != null || c.Treatment != null).WithMessage("Choose a category or what to count them as.");
        }
    }

    public class BulkUpdateTransactionsCommandHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<BulkUpdateTransactionsCommand, int>
    {
        public Task<int> Handle(BulkUpdateTransactionsCommand request, CancellationToken cancellationToken) =>
            transactions.BulkUpdateAsync(principal.UserId!, request.Ids, request.CategoryId, request.Treatment, cancellationToken);
    }

    /// <summary>Hides a category; with a target, its transactions, rules and budget lines move there first.</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record ArchiveCategoryCommand(int CategoryId, int? MergeIntoCategoryId) : IRequest<Unit>;

    public class ArchiveCategoryCommandHandler(ICategoryService categories, IClaimsPrincipalAccessor principal)
        : IRequestHandler<ArchiveCategoryCommand, Unit>
    {
        public async Task<Unit> Handle(ArchiveCategoryCommand request, CancellationToken cancellationToken)
        {
            await categories.ArchiveCategoryAsync(principal.UserId!, request.CategoryId, request.MergeIntoCategoryId);
            return Unit.Value;
        }
    }

    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetCategoryRulesQuery : IRequest<List<CategoryRuleModel>>;

    public class GetCategoryRulesQueryHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<GetCategoryRulesQuery, List<CategoryRuleModel>>
    {
        public Task<List<CategoryRuleModel>> Handle(GetCategoryRulesQuery request, CancellationToken cancellationToken) =>
            transactions.GetRulesAsync(principal.UserId!, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveCategoryRuleCommand(CategoryRuleModel Rule, bool ApplyToPast) : IRequest<RuleSaveResult>;

    public class SaveCategoryRuleCommandValidator : AbstractValidator<SaveCategoryRuleCommand>
    {
        public SaveCategoryRuleCommandValidator()
        {
            RuleFor(c => c.Rule).NotNull();
            RuleFor(c => c.Rule.Value).NotEmpty().MaximumLength(200).When(c => c.Rule != null);
            RuleFor(c => c.Rule.Field).IsInEnum().When(c => c.Rule != null);
            RuleFor(c => c.Rule.Match).IsInEnum().When(c => c.Rule != null);
            RuleFor(c => c.Rule.Direction).IsInEnum().When(c => c.Rule != null);
        }
    }

    public class SaveCategoryRuleCommandHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<SaveCategoryRuleCommand, RuleSaveResult>
    {
        public Task<RuleSaveResult> Handle(SaveCategoryRuleCommand request, CancellationToken cancellationToken) =>
            transactions.SaveRuleAsync(principal.UserId!, request.Rule, request.ApplyToPast, cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record DeleteCategoryRuleCommand(int CategoryRuleId) : IRequest<Unit>;

    public class DeleteCategoryRuleCommandHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<DeleteCategoryRuleCommand, Unit>
    {
        public async Task<Unit> Handle(DeleteCategoryRuleCommand request, CancellationToken cancellationToken)
        {
            await transactions.DeleteRuleAsync(principal.UserId!, request.CategoryRuleId, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>How many transactions a rule would change, before saving it.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record PreviewCategoryRuleQuery(CategoryRuleModel Rule) : IRequest<int>;

    public class PreviewCategoryRuleQueryHandler(ITransactionService transactions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<PreviewCategoryRuleQuery, int>
    {
        public Task<int> Handle(PreviewCategoryRuleQuery request, CancellationToken cancellationToken) =>
            transactions.PreviewRuleAsync(principal.UserId!, request.Rule, cancellationToken);
    }
}
