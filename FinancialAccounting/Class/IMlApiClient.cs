using System.Collections.Generic;
using System.Threading.Tasks;
using FinancialAccounting.Class.Models;

namespace FinancialAccounting.Class
{
    public interface IMlApiClient
    {
        Task<List<TransactionRecord>> CategorizeAsync(List<TransactionRecord> transactions);
        Task<MlCategorySuggestion> EnrichWebAsync(TransactionRecord transaction, List<string> availableCategories);
        Task<bool> SendFeedbackAsync(List<TransactionRecord> transactions);
    }
}
