using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using UglyToad.PdfPig.Content;
using Word = DocumentFormat.OpenXml.Wordprocessing;
using Drawing = DocumentFormat.OpenXml.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using MailKit.Net.Smtp;
using MimeKit;

namespace FinancialAccounting
{
    /// <summary>
    /// Логика взаимодействия для ReportsWindow.xaml
    /// </summary>
    public partial class ReportsWindow : Window
    {
        private readonly int _userId;
        private readonly int _accountId;
        public ReportsWindow(int accountId, int userId)
        {
            InitializeComponent();
            _userId = userId;
            _accountId = accountId;
            LoadCategories();
        }
        private string GetAccountNumberById(int accountId)
        {
            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT accountnumber FROM accounts WHERE id = @accountId";
                    cmd.Parameters.AddWithValue("accountId", accountId);

                    object result = cmd.ExecuteScalar();
                    if (result != null)
                        return result.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при получении номера счёта: " + ex.Message);
            }

            return string.Empty;
        }
        private string GetUserEmailById(int userId)
        {
            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT email FROM users WHERE id = @id";
                    cmd.Parameters.AddWithValue("id", userId);

                    object result = cmd.ExecuteScalar();
                    return result?.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при получении email: " + ex.Message);
                return null;
            }
        }
        private void LoadCategories()
        {
            try
            {
                var uniqueCategories = new HashSet<string> { "Все" };

                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT DISTINCT name FROM categories ORDER BY name ASC";

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            uniqueCategories.Add(reader.GetString(0));
                        }
                    }
                }

                CategoryComboBox.ItemsSource = uniqueCategories.ToList();
                CategoryComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке категорий: " + ex.Message);
            }
        }



        private void ApplyFilters_Click(object sender, RoutedEventArgs e)
        {
            LoadFilteredData();
        }

        private void LoadFilteredData()
        {
            List<ReportRecord> filteredData = new List<ReportRecord>();

            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    StringBuilder query = new StringBuilder(@"
                SELECT t.id, t.date, t.type, c.name AS category, t.amount, t.description
                FROM transactions t
                LEFT JOIN categories c ON t.categoryid = c.id
                WHERE t.accountid = @accountId");

                    cmd.Parameters.AddWithValue("accountId", _accountId);

                    // Добавляем фильтрацию по дате
                    if (StartDatePicker.SelectedDate.HasValue)
                    {
                        query.Append(" AND t.date >= @startDate");
                        cmd.Parameters.AddWithValue("startDate", StartDatePicker.SelectedDate.Value.Date);
                    }

                    if (EndDatePicker.SelectedDate.HasValue)
                    {
                        query.Append(" AND t.date <= @endDate");
                        cmd.Parameters.AddWithValue("endDate", EndDatePicker.SelectedDate.Value.Date);
                    }

                    // Фильтр по типу транзакции
                    if (TypeComboBox.SelectedItem is ComboBoxItem typeItem)
                    {
                        string selectedType = typeItem.Content.ToString();
                        if (selectedType == "Доход")
                        {
                            query.Append(" AND t.type = 'Income'");
                        }
                        else if (selectedType == "Расход")
                        {
                            query.Append(" AND t.type = 'Expense'");
                        }
                    }

                    // Фильтр по категории
                    if (CategoryComboBox.SelectedItem is string selectedCategory && selectedCategory != "Все")
                    {
                        query.Append(" AND c.name = @categoryName");
                        cmd.Parameters.AddWithValue("categoryName", selectedCategory);
                    }


                    cmd.CommandText = query.ToString();

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            filteredData.Add(new ReportRecord
                            {
                                Id = reader.GetInt32(0),
                                Date = reader.GetDateTime(1).ToString("dd.MM.yyyy"),
                                Type = reader.GetString(2),
                                Category = reader.IsDBNull(3) ? "Без категории" : reader.GetString(3),
                                Amount = reader.GetDecimal(4),
                                Description = reader.IsDBNull(5) ? "" : reader.GetString(5)
                            });
                        }
                    }
                }

                UpdateTotals(filteredData);
                ReportDataGrid.ItemsSource = filteredData;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке данных: " + ex.Message);
            }
        }

        private void UpdateTotals(List<ReportRecord> filteredData)
        {
            decimal totalIncome = filteredData
                .Where(r => r.Type == "Income")
                .Sum(r => r.Amount);

            decimal totalExpense = Math.Abs(
                filteredData
                    .Where(r => r.Type == "Expense")
                    .Sum(r => r.Amount)
            );

            decimal balance = totalIncome - totalExpense;

            IncomeTotalTextBlock.Text = $"Доходы: {totalIncome:F2} ₽";
            ExpenseTotalTextBlock.Text = $"Расходы: {totalExpense:F2} ₽";
            BalanceTotalTextBlock.Text = $"Итог: {balance:F2} ₽";
        }

        private void DeleteSelectedRecord_Click(object sender, RoutedEventArgs e)
        {
            if (!(ReportDataGrid.SelectedItem is ReportRecord selectedRecord))
            {
                MessageBox.Show("Сначала выберите запись для удаления.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                $"Удалить запись от {selectedRecord.Date} на сумму {selectedRecord.Amount:F2}?",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM transactions WHERE id = @id AND accountid = @accountId";
                    cmd.Parameters.AddWithValue("id", selectedRecord.Id);
                    cmd.Parameters.AddWithValue("accountId", _accountId);

                    int affectedRows = cmd.ExecuteNonQuery();
                    if (affectedRows == 0)
                    {
                        MessageBox.Show("Запись не найдена или уже была удалена.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                LoadFilteredData();
                MessageBox.Show("Запись удалена.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при удалении записи: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedFormat = (ExportFormatComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            switch (selectedFormat)
            {
                case "CSV":
                    ExportToCsv();
                    break;
                case "Word":
                    ExportToWord();
                    break;
                case "PDF":
                    ExportToPDF();
                    break;
                default:
                    MessageBox.Show("Пожалуйста, выберите формат экспорта.");
                    break;
            }
        }
        private void ExportToCsv()
        {
            if (ReportDataGrid.ItemsSource is List<ReportRecord> records && records.Any())
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = "Отчёт",
                    DefaultExt = ".csv",
                    Filter = "CSV файлы (.csv)|*.csv"
                };

                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        using (var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8))
                        {
                            string fullAccountNumber = GetAccountNumberById(_accountId);
                            string exportDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                            writer.WriteLine($"Дата экспорта: {exportDate}");
                            writer.WriteLine($"Счёт: {fullAccountNumber}"); 
                            writer.WriteLine();                           
                            writer.WriteLine("Дата;Тип;Категория;Сумма;Описание");

                            foreach (var record in records)
                            {
                                writer.WriteLine($"{record.Date};{record.Type};{record.Category};{record.Amount};\"{record.Description}\"");
                            }
                        }

                        MessageBox.Show("Экспорт завершён успешно!", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Ошибка при экспорте: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Нет данных для экспорта!", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportToWord()
        {
            if (ReportDataGrid.ItemsSource is List<ReportRecord> records && records.Any())
            {
                var dialog = new SaveFileDialog
                {
                    FileName = "Отчёт_" + DateTime.Now.ToString("yyyyMMdd") + ".docx",
                    
                    Filter = "Word документы (.docx)|*.docx"
                };

                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        using (WordprocessingDocument wordDocument =
                            WordprocessingDocument.Create(dialog.FileName, WordprocessingDocumentType.Document))
                        {
                            MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();
                            mainPart.Document = new Word.Document();
                            Word.Body body = mainPart.Document.AppendChild(new Word.Body());
                           
                            AddHeaderParagraph(body, $"Дата экспорта: {DateTime.Now:dd.MM.yyyy HH:mm}");
                            AddHeaderParagraph(body, $"Счёт: {GetAccountNumberById(_accountId)}");
                            body.AppendChild(new Word.Paragraph(new Word.Run(new Word.Text(""))));

                            
                            Word.Table table = new Word.Table();

                            Word.TableProperties tableProps = new Word.TableProperties(
                                new Word.TableBorders(
                                    new Word.TopBorder() { Val = Word.BorderValues.Single, Size = 4 },
                                    new Word.BottomBorder() { Val = Word.BorderValues.Single, Size = 4 },
                                    new Word.LeftBorder() { Val = Word.BorderValues.Single, Size = 4 },
                                    new Word.RightBorder() { Val = Word.BorderValues.Single, Size = 4 },
                                    new Word.InsideHorizontalBorder() { Val = Word.BorderValues.Single, Size = 4 },
                                    new Word.InsideVerticalBorder() { Val = Word.BorderValues.Single, Size = 4 }
                                )
                            );
                            table.AppendChild(tableProps);

                            // Заголовки
                            table.AppendChild(CreateHeaderRow("Дата", "Тип", "Категория", "Сумма", "Описание"));

                            // Данные
                            foreach (var record in records)
                            {
                                table.AppendChild(CreateDataRow(
                                    record.Date,
                                    record.Type == "Income" ? "Доход" : "Расход",
                                    record.Category,
                                    record.Amount.ToString("F2") + " ₽",
                                    record.Description
                                ));
                            }

                            body.AppendChild(table);

                            AddTotalParagraph(body, IncomeTotalTextBlock.Text);
                            AddTotalParagraph(body, ExpenseTotalTextBlock.Text);
                            AddTotalParagraph(body, BalanceTotalTextBlock.Text);
                        }

                        MessageBox.Show("Экспорт завершён успешно!", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка при экспорте: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                MessageBox.Show("Нет данных для экспорта!", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AddHeaderParagraph(Word.Body body, string text)
        {
            var paragraph = new Word.Paragraph(
                new Word.ParagraphProperties(
                    new Word.Justification() { Val = Word.JustificationValues.Center },
                    new Word.SpacingBetweenLines() { After = "100" }
                ),
                new Word.Run(
                    new Word.RunProperties(new Word.Bold()),
                    new Word.Text(text)
                )
            );
            body.AppendChild(paragraph);
        }

        private Word.TableRow CreateHeaderRow(params string[] headers)
        {
            Word.TableRow row = new Word.TableRow();
            foreach (string header in headers)
            {
                row.Append(new Word.TableCell(
                    new Word.Paragraph(
                        new Word.Run(
                            new Word.RunProperties(new Word.Bold()),
                            new Word.Text(header)
                        )
                    )
                ));
            }
            return row;
        }

        private Word.TableRow CreateDataRow(params string[] values)
        {
            Word.TableRow row = new Word.TableRow();
            foreach (string value in values)
            {
                row.Append(new Word.TableCell(
                    new Word.Paragraph(
                        new Word.Run(
                            new Word.Text(value)
                        )
                    )
                ));
            }
            return row;
        }

        private void AddTotalParagraph(Word.Body body, string text)
        {
            var paragraph = new Word.Paragraph(
                new Word.ParagraphProperties(
                    new Word.SpacingBetweenLines() { After = "100" }
                ),
                new Word.Run(
                    new Word.RunProperties(new Word.Bold()),
                    new Word.Text(text)
                )
            );
            body.AppendChild(paragraph);
        }

        private void ExportToPDF()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                FileName = "Отчёт_" + DateTime.Now.ToString("yyyyMMdd") + ".pdf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = saveFileDialog.FileName;                   
                    PdfDocument document = new PdfDocument();
                    document.Info.Title = "Финансовый отчёт";
                    PdfPage page = document.AddPage();
                    XGraphics gfx = XGraphics.FromPdfPage(page);
                    XFont font = new XFont("Verdana", 10); // Без указания XFontStyle
                    XFont boldFont = new XFont("Verdana", 10, XFontStyleEx.Bold);

                    double margin = 40;
                    double y = margin;
                    double lineHeight = 20;

                    // Заголовок
                    gfx.DrawString("Финансовый отчёт", new XFont("Verdana", 14, XFontStyleEx.Bold), XBrushes.Black, new XPoint(margin, y));
                    y += lineHeight;

                    // Дата и номер счёта
                    string date = DateTime.Now.ToString("dd.MM.yyyy");
                    string accountNumber = GetAccountNumberById(_accountId);
                    gfx.DrawString($"Дата: {date}", font, XBrushes.Black, new XPoint(margin, y));
                    gfx.DrawString($"Счёт: {accountNumber}", font, XBrushes.Black, new XPoint(page.Width / 2, y));
                    y += lineHeight * 2;

                    // Заголовки таблицы
                    string[] headers = { "Дата", "Тип", "Категория", "Сумма", "Описание" };
                    double[] columnWidths = { 70, 70, 100, 70, 200 };
                    double x = margin;

                    for (int i = 0; i < headers.Length; i++)
                    {
                        gfx.DrawString(headers[i], boldFont, XBrushes.Black, new XRect(x, y, columnWidths[i], lineHeight), XStringFormats.TopLeft);
                        x += columnWidths[i];
                    }

                    y += lineHeight;
                    List<ReportRecord> records = ReportDataGrid.Items.Cast<ReportRecord>().ToList();
                    decimal totalIncome = records
                        .Where(r => r.Type == "Income")
                        .Sum(r => r.Amount);

                    decimal totalExpense = records
                        .Where(r => r.Type == "Expense")
                        .Sum(r => Math.Abs(r.Amount)); // сумма по модулю

                    decimal balance = totalIncome - totalExpense;
                    // Данные из таблицы
                    foreach (ReportRecord record in ReportDataGrid.Items)
                    {
                        x = margin;
                        gfx.DrawString(record.Date, font, XBrushes.Black, new XRect(x, y, columnWidths[0], lineHeight), XStringFormats.TopLeft); x += columnWidths[0];
                        gfx.DrawString(record.Type, font, XBrushes.Black, new XRect(x, y, columnWidths[1], lineHeight), XStringFormats.TopLeft); x += columnWidths[1];
                        gfx.DrawString(record.Category, font, XBrushes.Black, new XRect(x, y, columnWidths[2], lineHeight), XStringFormats.TopLeft); x += columnWidths[2];
                        gfx.DrawString(record.Amount.ToString("F2"), font, XBrushes.Black, new XRect(x, y, columnWidths[3], lineHeight), XStringFormats.TopLeft); x += columnWidths[3];
                        gfx.DrawString(record.Description, font, XBrushes.Black, new XRect(x, y, columnWidths[4], lineHeight), XStringFormats.TopLeft);

                        y += lineHeight;

                        // Переход на новую страницу при необходимости
                        if (y + lineHeight > page.Height - margin)
                        {
                            page = document.AddPage();
                            gfx = XGraphics.FromPdfPage(page);
                            y = margin;
                        }
                    }
                    y += lineHeight;

                    if (y + lineHeight * 3 > page.Height - margin)
                    {
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        y = margin;
                    }

                    gfx.DrawString("Итоговые значения:", boldFont, XBrushes.Black, new XPoint(margin, y)); y += lineHeight;
                    gfx.DrawString($"Доход: {totalIncome:F2} ₽", font, XBrushes.Black, new XPoint(margin, y)); y += lineHeight;
                    gfx.DrawString($"Расход: {totalExpense:F2} ₽", font, XBrushes.Black, new XPoint(margin, y)); y += lineHeight;
                    gfx.DrawString($"Баланс: {balance:F2} ₽", font, XBrushes.Black, new XPoint(margin, y));
                    document.Save(filePath);
                    MessageBox.Show("Экспорт в PDF завершён успешно!");
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при экспорте в PDF: " + ex.Message);
                }
            }
        }

        private void SendEmailReport_Click(object sender, RoutedEventArgs e)
        {
            string userEmail = GetUserEmailById(_userId);       
            if (string.IsNullOrEmpty(userEmail))
            {
                MessageBox.Show("Не удалось получить email пользователя.");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "PDF файлы (*.pdf)|*.pdf|Документы Word (*.docx)|*.docx|Все файлы (*.*)|*.*",
                Title = "Выберите файл отчёта для отправки"
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedFilePath = dialog.FileName;

                try
                {
                    var message = new MimeMessage();
                    message.From.Add(new MailboxAddress("Финансовый учёт", "misha.barinow2016@yandex.ru")); 
                    message.To.Add(new MailboxAddress("", userEmail));
                    message.Subject = "Ваш финансовый отчёт";

                    var builder = new BodyBuilder
                    {
                        TextBody = $"Здравствуйте!\n\nВаш финансовый отчёт от {DateTime.Now:dd.MM.yyyy} во вложении."
                    };

                    builder.Attachments.Add(selectedFilePath);
                    message.Body = builder.ToMessageBody();

                    using (var client = new SmtpClient())
                    {
                        client.Connect("smtp.yandex.ru", 587, MailKit.Security.SecureSocketOptions.StartTls);
                        client.Authenticate("misha.barinow2016@yandex.ru", "tenftaiqbsgdqxlf");
                        client.Send(message);
                        client.Disconnect(true);
                    }

                    MessageBox.Show("Файл успешно отправлен на почту!", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при отправке письма: " + ex.Message);
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            string username = GetUsernameByUserId(_userId);
            var mainWindow = new MainWindow(username); // или передайте сюда username, если надо
            mainWindow.Show();
            this.Close();
        }
        private string GetUsernameByUserId(int userId)
        {
            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT login FROM users WHERE id = @userId";
                    cmd.Parameters.AddWithValue("userId", userId);

                    object result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        return result.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при получении имени пользователя: " + ex.Message);
            }

            return string.Empty; // Если не найдено
        }
    }
}
public class ReportRecord
{
    public int Id { get; set; }
    public string Date { get; set; }
    public string Type { get; set; }
    public string Category { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
}

