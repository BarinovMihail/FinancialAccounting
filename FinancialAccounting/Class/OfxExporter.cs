using FinancialAccounting.Class.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace FinancialAccounting
{
    public static class OfxExporter
    {
        public static void ExportToFile(ObservableCollection<TransactionRecord> transactions, Action<int, int> progressCallback = null)
        {
            if (transactions == null || transactions.Count == 0)
            {
                MessageBox.Show("Нет данных для экспорта.");
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "OFX files (*.ofx)|*.ofx",
                Title = "Сохранить как OFX"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            try
            {
                string ofxContent = GenerateOfx(transactions, progressCallback);
                File.WriteAllText(saveFileDialog.FileName, ofxContent, Encoding.UTF8);
                MessageBox.Show("Файл OFX успешно сохранён.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}");
            }
        }


        public static string GenerateOfx(ObservableCollection<TransactionRecord> transactions, Action<int, int> progressCallback = null)
        {
            var sb = new StringBuilder();

            sb.AppendLine("OFXHEADER:100");
            sb.AppendLine("DATA:OFXSGML");
            sb.AppendLine("VERSION:102");
            sb.AppendLine("SECURITY:NONE");
            sb.AppendLine("ENCODING:UTF-8");
            sb.AppendLine("CHARSET:UTF-8");
            sb.AppendLine("COMPRESSION:NONE");
            sb.AppendLine("OLDFILEUID:NONE");
            sb.AppendLine("NEWFILEUID:NONE");
            sb.AppendLine();
            sb.AppendLine("<OFX>");
            sb.AppendLine("  <BANKMSGSRSV1>");
            sb.AppendLine("    <STMTTRNRS>");
            sb.AppendLine("      <TRNUID>1");
            sb.AppendLine("      <STATUS>");
            sb.AppendLine("        <CODE>0");
            sb.AppendLine("        <SEVERITY>INFO");
            sb.AppendLine("      </STATUS>");
            sb.AppendLine("      <STMTRS>");
            sb.AppendLine("        <CURDEF>RUB");
            sb.AppendLine("        <BANKTRANLIST>");

            var dates = transactions
                .Select(t => DateTime.TryParse(t.Date, out var dt) ? dt : DateTime.Now)
                .OrderBy(d => d)
                .ToList();

            string dateStart = dates.FirstOrDefault().ToString("yyyyMMdd");
            string dateEnd = dates.LastOrDefault().ToString("yyyyMMdd");

            sb.AppendLine($"          <DTSTART>{dateStart}");
            sb.AppendLine($"          <DTEND>{dateEnd}");

            int total = transactions.Count;
            for (int i = 0; i < total; i++)
            {
                var tx = transactions[i];
                DateTime date = DateTime.TryParse(tx.Date, out var parsedDate) ? parsedDate : DateTime.Now;
                string amount = tx.Amount.Replace(" ", "").Replace(",", ".").Replace("+", "");
                string typeValue = tx.Type ?? "Expense";
                string type = string.Equals(typeValue, "Income", StringComparison.OrdinalIgnoreCase) ? "CREDIT" : "DEBIT";
                string description = tx.Description ?? "NO DESCRIPTION";

                sb.AppendLine("          <STMTTRN>");
                sb.AppendLine($"            <TRNTYPE>{type}");
                sb.AppendLine($"            <DTPOSTED>{date:yyyyMMdd}");
                sb.AppendLine($"            <TRNAMT>{amount}");
                sb.AppendLine($"            <FITID>{Guid.NewGuid()}");
                sb.AppendLine($"            <NAME>{description}");
                sb.AppendLine("          </STMTTRN>");
         
                progressCallback?.Invoke(i + 1, total);
            }

            sb.AppendLine("        </BANKTRANLIST>");
            sb.AppendLine("        <LEDGERBAL>");
            sb.AppendLine("          <BALAMT>0.00");
            sb.AppendLine($"          <DTASOF>{DateTime.Now:yyyyMMdd}");
            sb.AppendLine("        </LEDGERBAL>");
            sb.AppendLine("      </STMTRS>");
            sb.AppendLine("    </STMTTRNRS>");
            sb.AppendLine("  </BANKMSGSRSV1>");
            sb.AppendLine("</OFX>");

            return sb.ToString();
        }

    }
}
