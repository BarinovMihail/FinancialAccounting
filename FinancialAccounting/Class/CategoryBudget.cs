namespace FinancialAccounting
{
    public class CategoryBudget
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public decimal Amount { get; set; }
    }
}
