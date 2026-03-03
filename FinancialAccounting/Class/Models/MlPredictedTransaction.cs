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
    }
}
