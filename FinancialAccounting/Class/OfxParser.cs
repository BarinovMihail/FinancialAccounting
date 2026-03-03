using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace FinancialAccounting
{
    public static class OfxParser
    {
        public static ObservableCollection<TransactionRecord> ParseOfx(string filePath)
        {
            var transactions = new ObservableCollection<TransactionRecord>();
            string[] lines = File.ReadAllLines(filePath);

            string currentDate = "";
            string currentAmount = "";
            string currentName = "";
            string currentType = "Expense";

            foreach (var line in lines)
            {
                if (line.Contains("<TRNTYPE>"))
                {
                    var type = line.Replace("<TRNTYPE>", "").Trim().ToUpper();
                    currentType = (type == "CREDIT") ? "Income" : "Expense";
                }

                else if (line.Contains("<DTPOSTED>"))
                {
                    string rawDate = line.Replace("<DTPOSTED>", "").Trim();
                    if (DateTime.TryParseExact(rawDate.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        currentDate = dt.ToString("dd.MM.yyyy");
                    }
                }

                else if (line.Contains("<TRNAMT>"))
                {
                    decimal value;
                    currentAmount = line.Replace("<TRNAMT>", "").Trim();
                    if (decimal.TryParse(currentAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                    {
                        currentAmount = (value > 0 ? "+" : "") + value.ToString("N2", new CultureInfo("ru-RU"));
                    }
                }

                else if (line.Contains("<NAME>"))
                {
                    currentName = line.Replace("<NAME>", "").Trim();
                }

                else if (line.Contains("</STMTTRN>")) 
                {
                    transactions.Add(new TransactionRecord
                    {
                        Date = currentDate,
                        Amount = currentAmount,
                        Description = currentName,
                        Category = "Uncategorized", 
                        Type = currentType,
                        Balance = ""
                    });                  
                    currentDate = "";
                    currentAmount = "";
                    currentName = "";
                    currentType = "Expense";
                }
            }

            return transactions;
        }
    }
}
