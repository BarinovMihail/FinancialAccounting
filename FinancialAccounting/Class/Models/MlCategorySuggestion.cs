namespace FinancialAccounting.Class.Models
{
    public class MlCategorySuggestion
    {
        public string category { get; set; }
        public double confidence { get; set; }
        public string source { get; set; }
        public string reason { get; set; }
    }
}
