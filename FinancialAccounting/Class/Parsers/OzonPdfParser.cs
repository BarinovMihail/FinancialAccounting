using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FinancialAccounting.Class.Parsers
{
    public static class OzonPdfParser
    {
        public static ObservableCollection<Models.TransactionRecord> ParsePdfText(string rawText)
        {
            var transactions = new ObservableCollection<Models.TransactionRecord>();

            if (string.IsNullOrWhiteSpace(rawText))
                return transactions;

            try
            {

                var pattern = @"(\d{2}\.\d{2}\.\d{4})" +              // Дата операции (группа 1)
                             @"\d{2}:\d{2}:\d{2}" +                   // Время (HH:MM:SS)
                             @"\d{10}" +                              // Номер документа (10 цифр)
                             @"(.*?)" +                               // Описание/Назначение платежа (группа 2)
                             @"([+-]?\s*[\d\s]+\.\d{2}\s*₽)" +       // Первая сумма (группа 3)
                             @"[+-]?\s*[\d\s]+\.\d{2}\s*₽" +         // Вторая сумма (пропускаем)
                             @"(?=\d{2}\.\d{2}\.\d{4}|Итого|С\s+уважением|$)"; 

                var regex = new Regex(pattern, RegexOptions.Singleline);
                var matches = regex.Matches(rawText);

                foreach (Match match in matches)
                {
                    try
                    {
                        string date = match.Groups[1].Value;
                        string description = match.Groups[2].Value.Trim();
                        string amountStr = match.Groups[3].Value;

                        description = CleanDescription(description);

                        if (IsInvalidDescription(description))
                            continue;

                        decimal amountValue = ParseAmount(amountStr);
                        string type = amountValue >= 0 ? "Income" : "Expense";

                        string formattedAmount = amountValue >= 0
                            ? "+" + amountValue.ToString("N2", new CultureInfo("ru-RU"))
                            : amountValue.ToString("N2", new CultureInfo("ru-RU"));

                        transactions.Add(new TransactionRecord
                        {
                            Date = date,
                            Category = "",
                            Amount = formattedAmount,
                            Description = description,
                            Balance = "",
                            Type = type
                        });
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            catch (Exception)
            {
              
            }

            return transactions;
        }

        private static bool IsInvalidDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return true;

            string[] invalidPhrases = new[]
            {
                "Итого зачислений",
                "Итого списаний",
                "С уважением",
                "Руководитель",
                "Корольков",
                "мидл-офисных"
            };

            foreach (var phrase in invalidPhrases)
            {
                if (description.Contains(phrase))
                    return true;
            }

            if (description.Length > 200)
                return true;

            return false;
        }

        private static string CleanDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "";

            string[] cutPhrases = { "Итого", "С уважением" };
            foreach (var phrase in cutPhrases)
            {
                int cutIndex = description.IndexOf(phrase);
                if (cutIndex > 0)
                    description = description.Substring(0, cutIndex);
            }

            description = Regex.Replace(description, @"[+-]?\s*[\d\s]+\.\d{2}\s*₽", "");

            description = Regex.Replace(description, @"\d{2}\.\d{2}\.\d{4}", "");
            description = Regex.Replace(description, @"\d{2}:\d{2}:\d{2}", "");
            description = Regex.Replace(description, @"\d{10,}", "");

            description = Regex.Replace(description, @"([а-я])([А-Я])", "$1 $2");
            description = Regex.Replace(description, @"([a-z])([A-Z])", "$1 $2");
            description = Regex.Replace(description, @"([а-яА-Я])([a-zA-Z])", "$1 $2");
            description = Regex.Replace(description, @"([a-zA-Z])([а-яА-Я])", "$1 $2");

            description = description.Replace("товаров/услуг", "товаров/услуг ");
            description = description.Replace("наПлатформе", "на Платформе");

            while (description.Contains("  "))
                description = description.Replace("  ", " ");

            if (description.Length > 150)
                description = description.Substring(0, 150).Trim();

            return description.Trim();
        }

        private static decimal ParseAmount(string amountStr)
        {
            if (string.IsNullOrWhiteSpace(amountStr))
                return 0;

            string cleaned = amountStr.Replace("₽", "")
                                      .Replace(" ", "")
                                      .Replace("\u00A0", "")
                                      .Replace(",", ".")
                                      .Trim();

            if (decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            return 0;
        }
    }
}
