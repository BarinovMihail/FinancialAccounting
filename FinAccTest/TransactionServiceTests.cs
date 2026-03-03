using FinancialAccounting.Class;
using FinancialAccounting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Collections.ObjectModel;
using FinancialAccounting.Class.Models;

namespace FinAccTest
{
    public class TransactionServiceTests
    {
        [Fact]
        public void SaveTransactions_ReturnsError_WhenEmpty()
        {
            string username = "qwert";
            int accountId = 1;
            var mockDb = new Mock<DatabaseManager>();
            var service = new TransactionService(mockDb.Object, username, accountId);

            var result = service.SaveTransactions(new List<TransactionRecord>(), "user", 1);

            Assert.Equal("Нет данных для сохранения.", result);
        }

        [Fact]
        public void SaveTransactions_ShouldInsertData_WhenValidInput()
        {
            string username = "qwert"; 
            int accountId = 1;
            var db = new DatabaseManager();
            var service = new TransactionService(db, username, accountId);
            var transactions = new List<TransactionRecord>
        {
            new TransactionRecord
            {
                Date = "01.01.2024",
                Category = "Тестовая категория",
                Amount = "+1000,00",
                Description = "Тестовая транзакция",
                Balance = "1000,00"
            }
        };                    
          
            string result = service.SaveTransactions(transactions, username, accountId);

            Assert.Equal("OK", result);
        }
    }
}
