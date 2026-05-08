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

                    desc = CleanupDescription(desc);

                    if (transactions.Any())
                    {
                        transactions.Last().Description += " " + desc;
                        transactions.Last().Description = CleanupDescription(transactions.Last().Description);
                    }
                }
            }
           
            return transactions;
        }

        private static string CleanupDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            string cleaned = Regex.Replace(description.Trim(), @"\s+", " ");
            cleaned = Regex.Replace(
                cleaned,
                @"\s*\.?\s*Операция\s+по\s*карте\s+\*{2,}\d{2,4}.*$",
                "",
                RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(
                cleaned,
                @"\s*\.?\s*Операция\s+покарте\s+\*{2,}\d{2,4}.*$",
                "",
                RegexOptions.IgnoreCase);

            cleaned = CutAtFirstMarker(cleaned, new[]
            {
                "Продолжение на следующей странице",
                "Для проверки подлинности документа",
                "Выписка по счёту дебетовой карты",
                "Выписка по счету дебетовой карты",
                "Страница",
                "ДАТА ОПЕРАЦИИ",
                "Дата обработки",
                "КАТЕГОРИЯ",
                "Описание операции",
                "СУММА В ВАЛЮТЕ СЧЁТА",
                "Сумма в валюте операции",
                "ОСТАТОК СРЕДСТВ",
                "ПАО Сбербанк",
                "Генеральная лицензия",
                "Денежные средства",
                "Дергунова",
                "Управляющий директор",
                "Дивизиона",
                "Дата формирования"
            });

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.', ';', ',');
            return cleaned;
        }

        private static string CutAtFirstMarker(string text, string[] markers)
        {
            int firstIndex = -1;

            foreach (var marker in markers)
            {
                int markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex >= 0 && (firstIndex < 0 || markerIndex < firstIndex))
                    firstIndex = markerIndex;
            }

            return firstIndex >= 0 ? text.Substring(0, firstIndex).Trim() : text;
        }
    }
}
