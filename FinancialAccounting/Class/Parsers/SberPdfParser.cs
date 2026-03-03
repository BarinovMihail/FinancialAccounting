using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FinancialAccounting
{
    public static class SberbankPdfParser
    {
        public static ObservableCollection<TransactionRecord> ParsePdfText(string rawText)
        {
            ObservableCollection<TransactionRecord> transactions = new ObservableCollection<TransactionRecord>();

            rawText = rawText.Replace('\u00A0', ' ');

            int idx = rawText.IndexOf("Расшифровка операций");
            if (idx >= 0)
                rawText = rawText.Substring(idx);       
                string[] records = Regex.Split(rawText, @"(?=\d{2}\.\d{2}\.\d{4})")
                                      .Where(r => !string.IsNullOrWhiteSpace(r))
                                      .Select(r => r.Trim())
                                      .ToArray();

            for (int i = 0; i < records.Length; i++)
            {
                string record = records[i];             
                string date = record.Substring(0, 10).Trim();
                string headerPart = record.Substring(21).Trim();

                var mAmount = Regex.Match(headerPart, @"[+\-]?\d{1,3}(?:[ ]\d{3})*,\d{2}");
                if (mAmount.Success)
                {
                    int amountIndex = mAmount.Index;
                    string category = headerPart.Substring(0, amountIndex).Trim();
                    string amount = mAmount.Value.Trim();
                    if(!amount.StartsWith("+") && !amount.StartsWith("-"))
                        amount = "-" + amount;
                    string afterAmount = headerPart.Substring(amountIndex + mAmount.Length).Trim();
                    var mBalance = Regex.Match(afterAmount, @"\d{1,3}(?:[ ]\d{3})*,\d{2}");
                    if (mBalance.Success)
                    {                       
                        string balance = mBalance.Value.Trim();
                        transactions.Add(new TransactionRecord
                        {
                            Date = date,
                            Category = category,
                            Amount = amount,
                            Balance = balance,
                            Type = amount.StartsWith("+") ? "Income" : "Expense",
                            Description = ""
                        });
                    }
                }
                else
                {
                    string desc = "";
                    if (record.Length > 10)
                        desc = record.Substring(10).Trim();

                    var descMatch = Regex.Match(desc, @"^(.*?\*{4}\d{4})");
                    if (descMatch.Success)
                    {
                        desc = descMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        string[] stopWords = { "ПАО Сбербанк", "Генеральная лицензия", "Денежные средства" };
                        foreach (var stopWord in stopWords)
                        {
                            int stopIndex = desc.IndexOf(stopWord, System.StringComparison.OrdinalIgnoreCase);
                            if (stopIndex >= 0)
                            {
                                desc = desc.Substring(0, stopIndex).Trim();
                                break;
                            }
                        }
                    }

                    if (transactions.Any())
                    {
                        transactions.Last().Description += " " + desc;
                        transactions.Last().Description = transactions.Last().Description.Trim();
                    }
                }
            }
           
            return transactions;
        }
    }
}