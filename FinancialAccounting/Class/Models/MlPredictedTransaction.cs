using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialAccounting.Class.Models
{
    public class MlPredictedTransaction
    {
        public string description { get; set; }
        public string amount { get; set; }
        public string date { get; set; }
        public string predicted_category { get; set; }
        public double confidence { get; set; }
        public string source { get; set; }
        public bool needs_review { get; set; }
        public List<MlCategorySuggestion> suggestions { get; set; }
        public string suggestion_reason { get; set; }
    }
}
