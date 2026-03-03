using FinancialAccounting;
using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using Xunit;

namespace FinAccTest
{
    public class OfxExporterTests
    {
        [Fact]
        public void GenerateOfx_WithValidTransactions_ReturnsValidOfx()
        {
            var transactions = new ObservableCollection<TransactionRecord>
            {
                new TransactionRecord
                {
                    Date = "2025-05-01",
                    Amount = "1500,20",
                    Type = "Income",
                    Description = "Bonus"
                },
                new TransactionRecord
                {
                    Date = "2025.05.03",
                    Amount = "300",
                    Type = "Expense",
                    Description = "Utilities"
                }
            };

            string result = OfxExporter.GenerateOfx(transactions);

            Assert.Contains("<OFX>", result);
            Assert.Contains("<CURDEF>RUB", result);
            Assert.Contains("<TRNTYPE>CREDIT", result);
            Assert.Contains("<TRNTYPE>DEBIT", result);
            Assert.Contains("<NAME>Bonus", result);
            Assert.Contains("<NAME>Utilities", result);
            Assert.Contains("<DTPOSTED>20250501", result);
            Assert.Contains("<DTPOSTED>20250503", result);
        }
    }
}
