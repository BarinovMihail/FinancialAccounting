using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FinancialAccounting.Class.Parsers
{
    public static class TinkoffPdfParser
    {
        public static ObservableCollection<TransactionRecord> ParsePdfText(string rawText)
        {
            var transactions = new ObservableCollection<TransactionRecord>();

            if (string.IsNullOrWhiteSpace(rawText))
                return transactions;

            try
            {
                var pattern = @"(\d{2}\.\d{2}\.\d{4})" +           // 1. Дата операции
                             @"\s*\d{2}:\d{2}" +                   // Время операции (игнорируем)
                             @"\s*\d{2}\.\d{2}\.\d{4}" +           // Дата списания (игнорируем)
                             @"\s*\d{2}:\d{2}" +                   // Время списания (игнорируем)
                             @"\s*([+-]?\s*[\d\s]+\.\d{2}\s*₽)" +  // 2. Сумма в валюте операции
                             @"\s*[+-]?\s*[\d\s]+\.\d{2}\s*₽" +    // Сумма в валюте карты (игнорируем)
                             @"\s*" +                              // Возможные пробелы перед описанием
                             @"(.*?)" +                            // 3. Описание (захватывает всё до номера карты)
                             @"(?:\d{4}|—)" +                      // Номер карты (4 цифры) или прочерк
                                                                   // Lookahead для остановки перед следующей записью
                             @"(?=\s*\d{2}\.\d{2}\.\d{4}|" +       // Следующая дата
                             @"\s*Дата\s+и\s+время|" +
                             @"\s*Пополнения:|" +
                             @"\s*Расходы:|" +
                             @"\s*\d+\s*₽Пополнения:|" +
                             @"\s*С\s+уважением|" +
                             @"\s*АО\s+«ТБанк»|" +
                             @"\s*Руководитель|" +
                             @"\s*$)";

                var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var matches = regex.Matches(rawText);

                foreach (Match match in matches)
                {
                    try
                    {
                        string date = match.Groups[1].Value;
                        string amountStr = match.Groups[2].Value;
                        string description = match.Groups[3].Value; 

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
                "Пополнения:", "Расходы:", "С уважением", "Руководитель",
                "АО «ТБанк»", "универсальная лицензия", "Банка России",
                "ИНН", "КПП", "Шадрина", "Операция в других"
            };

            foreach (var phrase in invalidPhrases)
            {
                if (description.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (description.Length > 250) // Чуть увеличили лимит, т.к. номера теперь сохраняются
                return true;

            return false;
        }

        private static string CleanDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return "";

            description = description.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");

            string[] cutPhrases = { "Пополнения:", "Расходы:", "С уважением", "АО «ТБанк»", "Руководитель" };
            foreach (var phrase in cutPhrases)
            {
                int cutIndex = description.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (cutIndex > 0)
                    description = description.Substring(0, cutIndex);
            }

            description = Regex.Replace(description, @"\d{2}\.\d{2}\.\d{4}", ""); // Даты
            description = Regex.Replace(description, @"\d{2}:\d{2}", "");         // Время
            description = Regex.Replace(description, @"[+-]?\s*\d[\d\s,.]+₽", ""); // Суммы с рублём

            description = description.Replace("—", "").Trim();


            description = Regex.Replace(description, @"([а-яА-Яa-zA-Z])(\d)", "$1 $2");

            description = Regex.Replace(description, @"(\d)([а-яА-Яa-zA-Z])", "$1 $2");

            description = Regex.Replace(description, @"([а-я])([А-Я])", "$1 $2");

            description = Regex.Replace(description, @"([A-Z])(VEL\.)", "$1 $2");
            description = Regex.Replace(description, @"([A-Z])(NOVGOROD)", "$1 $2");

            var replacements = new System.Collections.Generic.Dictionary<string, string>
    {
        { "пономеру", "по номеру" },
        { "надоговор", "на договор" },
        { "черезбанкомат", "через банкомат" },
        { "обычныепокупки", "обычные покупки" },
        { "упартнеров", "у партнеров" },
        { "Системабыстрых", "Система быстрых" },
        { "переводс", "перевод с" },
        { "TPPMOSCOW", "TPP MOSCOW" },
        { "NSPK MOSCOW", "NSPK MOSCOW" }, 
        { "TAXIMoscow", "TAXI Moscow" },
        { "NA_AVTOBUSTomsk", "NA_AVTOBUS Tomsk" },
        { "OSENVEL", "OSEN VEL" },
        { "KRASNOE&BELOEVEL", "KRASNOE&BELOE VEL" },
        { "FLAGMANVEL", "FLAGMAN VEL" },
        { "KUPAVVEL", "KUPAV VEL" },
        { "ANTINIANVEL", "ANTINIAN VEL" },
        { "MIKROSKOPIYAVEL", "MIKROSKOPIYA VEL" },
        { "LINGVISTIKAVEL", "LINGVISTIKA VEL" },
        { "CITYVEL", "CITY VEL" },
        { "BARKVEL", "BARK VEL" },
        { "OREKHOVVEL", "OREKHOV VEL" },
        { "GALAMARTVEL", "GALAMART VEL" },
        { "FARMVEL", "FARM VEL" },
        { "NOVGORODRUS", "NOVGOROD RUS" }
    };

            foreach (var kvp in replacements)
            {
                if (description.Contains(kvp.Key))
                {
                    description = description.Replace(kvp.Key, kvp.Value);
                }
            }
            description = Regex.Replace(description, @"\s+", " ");

            if (description.Length > 200)
                description = description.Substring(0, 200).Trim();

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

            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            return 0;
        }
    }
}
