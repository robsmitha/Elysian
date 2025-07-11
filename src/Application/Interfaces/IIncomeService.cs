using Elysian.Application.Features.Financial.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Elysian.Application.Interfaces
{
    public interface IIncomeService
    {
        Task<IncomeSourceModel> GetIncomeSourceAsync(int incomeSourceId);
        Task<List<IncomeSourceModel>> GetIncomeSourcesAsync(int institutionAccessItemId);
        Task<List<IncomePaymentModel>> GetIncomePaymentsAsync(int incomeSourceId);
        Task<List<IncomePaymentModel>> GetIncomePaymentsByInstitutionAccessItemIdAsync(int institutionAccessItemId);
        Task<IncomeSourceModel> AddIncomeSourceAsync(IncomeSourceModel dto);
        Task<IncomeSourceModel> UpdateIncomeSourceAsync(IncomeSourceModel dto);
        Task DeleteIncomeSourceAsync(int incomeSourceId);
        Task DeletePaymentByTransactionIdAsync(string transactionId);
        Task<IncomePaymentModel> AddIncomePaymentAsync(IncomePaymentModel dto);
        Task<IncomePaymentModel> UpdateIncomePaymentAsync(IncomePaymentModel dto);
        Task DeleteIncomePaymentAsync(int incomePaymentId);
    }
}
