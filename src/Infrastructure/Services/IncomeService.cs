using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    public class IncomeService(ElysianContext context, IMapper mapper) : IIncomeService
    {
        public async Task<List<IncomePaymentModel>> GetIncomePaymentsAsync(int incomeSourceId)
        {
            var incomePayments = await context.IncomePayments.Where(i => i.IncomeSourceId == incomeSourceId).ToListAsync();
            return mapper.Map<List<IncomePaymentModel>>(incomePayments);
        }

        public async Task<IncomeSourceModel> GetIncomeSourceAsync(int incomeSourceId)
        {
            var incomeSource = await context.IncomeSources.FirstOrDefaultAsync(b => b.IncomeSourceId == incomeSourceId);
            return mapper.Map<IncomeSourceModel>(incomeSource);
        }

        public async Task<List<IncomeSourceModel>> GetIncomeSourcesAsync(string userId)
        {
            var incomeSources = await context.IncomeSources.Where(b => b.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId)).ToListAsync();
            return mapper.Map<List<IncomeSourceModel>>(incomeSources);
        }

        public async Task<IncomeSourceModel> AddIncomeSourceAsync(IncomeSourceModel dto)
        {
            var incomeSource = mapper.Map<IncomeSource>(dto);
            await context.AddAsync(incomeSource);
            await context.SaveChangesAsync();
            return mapper.Map<IncomeSourceModel>(incomeSource);
        }

        public async Task DeleteIncomeSourceAsync(int incomeSourceId)
        {
            await context.IncomeSources.Where(i => i.IncomeSourceId == incomeSourceId).ExecuteDeleteAsync();
        }

        public async Task<IncomeSourceModel> UpdateIncomeSourceAsync(IncomeSourceModel dto)
        {
            var incomeSource = await context.IncomeSources.FindAsync(dto.IncomeSourceId);
            incomeSource.Name = dto.Name;
            incomeSource.Description = dto.Description;
            incomeSource.StartDate = dto.StartDate;
            incomeSource.EndDate = dto.EndDate;
            incomeSource.AmountDue = dto.AmountDue;
            incomeSource.PaymentFrequency = dto.PaymentFrequency;
            incomeSource.IncomeSourceType = dto.IncomeSourceType;
            incomeSource.ExpectedPaymentMemos = dto.ExpectedPaymentMemos;

            await context.SaveChangesAsync();
            return mapper.Map<IncomeSourceModel>(incomeSource);
        }

        public async Task<IncomePaymentModel> AddIncomePaymentAsync(IncomePaymentModel dto)
        {
            var incomePayment = mapper.Map<IncomePayment>(dto);
            await context.AddAsync(incomePayment);
            await context.SaveChangesAsync();
            return mapper.Map<IncomePaymentModel>(incomePayment);
        }

        public async Task<IncomePaymentModel> UpdateIncomePaymentAsync(IncomePaymentModel dto)
        {
            var incomePayment = await context.IncomePayments.FindAsync(dto.IncomePaymentId);
            incomePayment.PaymentMemo = dto.PaymentMemo;
            incomePayment.PaymentDate = dto.PaymentDate;
            incomePayment.Amount = dto.Amount;

            await context.SaveChangesAsync();
            return mapper.Map<IncomePaymentModel>(incomePayment);
        }

        public async Task DeleteIncomePaymentAsync(int incomePaymentId)
        {
            await context.IncomePayments.Where(i => i.IncomePaymentId == incomePaymentId).ExecuteDeleteAsync();
        }
    }
}
