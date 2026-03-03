using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialAccounting.Class.Models
{
    public class MlPredictResponse
    {
        public bool success { get; set; }
        public List<MlPredictedTransaction> results { get; set; }
    }
}
