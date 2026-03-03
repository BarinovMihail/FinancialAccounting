using System.Collections.Generic;
using System.Threading.Tasks;
using FinancialAccounting.Class.Models;

namespace FinancialAccounting.Class
{
    public interface IMlApiClient
    {
        Task<List<TransactionRecord>> CategorizeAsync(List<TransactionRecord> transactions);
        Task<bool> SendFeedbackAsync(List<TransactionRecord> transactions);
    }
}
