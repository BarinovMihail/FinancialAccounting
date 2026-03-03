using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialAccounting.Class.Models
{
    public class MlPredictRequest
    {
        public List<MlTransaction> transactions { get; set; }
    }
}
