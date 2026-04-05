using ExcelDataReader;
using FinancialAccounting.Class;
using FinancialAccounting.Class.Models;
using FinancialAccounting.Class.Parsers;
using Microsoft.Win32;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UglyToad.PdfPig;

namespace FinancialAccounting
{
    /// <summary>
    /// Логика взаимодействия для DataUploadWindow.xaml
    /// </summary>
    public partial class DataUploadWindow : Window
    {
        private readonly int _accountId;
        private readonly string _username;
        private string selectedFilePath;
        private readonly IMlApiClient _mlClient;
        private static readonly string[] ReceiptOcrLanguages = { "rus", "eng" };
        private static readonly string[] SupportedReceiptExtensions = { ".jpg", ".jpeg", ".png" };
        private const string ReceiptCategoryName = "Супермаркеты";
        private static readonly string[] ReceiptSummaryTokens =
        {
            "ИТОГ", "ИТОГО", "ВСЕГО", "СУММА", "СКИДКА", "СДАЧА", "НАЛИЧНЫМИ", "БЕЗНАЛИЧНЫМИ", "БЕЗНАЛ", "ПОЛУЧЕНО", "ПРИНЯТО", "ПОДЫТОГ"
        };
        private static readonly string[] ReceiptServiceTokens =
        {
            "ИНН", "ККТ", "ФН", "ФД", "ФП", "САЙТ ФНС", "NALOG.RU", "WWW.", "QR", "КАССИР", "КАССА", "СМЕНА",
            "ЧЕК:", "ЧЕК ", "ПРИХОД", "МЕСТО РАСЧЕТОВ", "АДРЕС", "КОД:", "ПРОДАВЕЦ", "СНО:", "РН ККТ", "ЗН ККТ",
            "ОФД", "СПАСИБО", "ГОРЯЧАЯ ЛИНИЯ", "ПОЛУЧИТЕ", "ПОДРОБНОСТИ", "МАГНИТИКИ", "СКРЕПЫШИ", "ШТРИХКОД"
        };

        // Коллекция для привязки к DataGrid
        private ObservableCollection<TransactionRecord> _transactions =
            new ObservableCollection<TransactionRecord>();
        public DataUploadWindow(int accountId, string username)
            : this(accountId, username, new MlApiClient())
        {
        }
        public DataUploadWindow(int accountId, string username, IMlApiClient mlClient)

        {
            InitializeComponent();
            _accountId = accountId;
            _username = username;
            TransactionsGrid.ItemsSource = _transactions;
            _mlClient = mlClient;
            LoadCategories();

            if (btnTrainModel != null)
                btnTrainModel.Visibility = Visibility.Collapsed;
        }


        private void LoadCategories()
        {
            CategoryComboBox.Items.Clear();
            CategoryComboBox.Items.Add(new ComboBoxItem { Content = "Все", Tag = null });

            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();
                using (var command = new NpgsqlCommand("SELECT id, name FROM categories ORDER BY name", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        CategoryComboBox.Items.Add(new ComboBoxItem
                        {
                            Content = reader["name"].ToString(),
                            Tag = reader.GetInt32(0)
                        });
                    }
                }
            }

            CategoryComboBox.SelectedIndex = 0;
        }
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|Excel Files|*.xls;*.xlsx;*.xlsm",
                Title = "Выберите PDF выписку или Excel файл"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                selectedFilePath = openFileDialog.FileName;
                txtFileName.Text = System.IO.Path.GetFileName(openFileDialog.FileName);
            }
        }

        private void RadioButton_Checked_1(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "OFX Files|*.ofx",
                Title = "Выберите OFX выписку"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                selectedFilePath = openFileDialog.FileName;
                txtFileName.Text = System.IO.Path.GetFileName(openFileDialog.FileName);
            }
        }

        private void RadioButton_Checked_2(object sender, RoutedEventArgs e)
        {
            TransactionsGrid.IsReadOnly = false;
            TransactionsGrid.CanUserAddRows = true;

            _transactions = new ObservableCollection<TransactionRecord>();
            TransactionsGrid.ItemsSource = _transactions;

            if (btnTrainModel != null) btnTrainModel.Visibility = Visibility.Collapsed;
        }

        private async void UploadReceipt_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения чеков|*.jpg;*.jpeg;*.png",
                Title = "Выберите изображение чека"
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            var receiptPath = openFileDialog.FileName;
            txtFileName.Text = Path.GetFileName(receiptPath);

            try
            {
                SetBusyState("Распознавание чека...", 15);
                var parseResult = await RecognizeReceiptAsync(receiptPath);
                AppendReceiptToTransactions(parseResult);

                ProgressBar.Value = 100;
                ProgressText.Text = "Чек успешно распознан.";
                await Task.Delay(400);

                MessageBox.Show(
                    $"Чек успешно распознан.\nДобавлено строк: {parseResult.Records.Count}.",
                    "Успех",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось распознать чек: {ex.Message}",
                    "Ошибка OCR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ResetBusyState();
            }
        }

        private async void Processing_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedFilePath))
            {
                MessageBox.Show("Выберите файл перед загрузкой!");
                return;
            }

            // При загрузке новых данных сбрасываем кнопку обучения
            if (btnTrainModel != null)
                btnTrainModel.Visibility = Visibility.Collapsed;

            ProgressBar.Visibility = Visibility.Visible;
            ProgressText.Text = "Обработка файла...";
            ProgressBar.Value = 10;

            await Task.Delay(100);
            string fileExtension = System.IO.Path.GetExtension(selectedFilePath).ToLower();

            try
            {
                if (fileExtension == ".pdf")
                {
                    string rawText = "";
                    using (var pdf = PdfDocument.Open(selectedFilePath))
                    {
                        int pageCount = pdf.NumberOfPages;
                        int currentPage = 0;

                        foreach (var page in pdf.GetPages())
                        {
                            rawText += "\n" + page.Text;
                            currentPage++;
                            ProgressBar.Value = 10 + (currentPage * 80 / pageCount);
                        }
                    }
                    string bank = DetectBank(rawText);
                    if (bank == "Sber")
                        _transactions = SberbankPdfParser.ParsePdfText(rawText);
                    else if (bank == "Tinkoff")
                        _transactions = TinkoffPdfParser.ParsePdfText(rawText);
                    else if (bank == "Ozon")
                        _transactions = OzonPdfParser.ParsePdfText(rawText);
                    else
                        MessageBox.Show("Не удалось определить банк.");
                }
                else if (fileExtension == ".xls" || fileExtension == ".xlsx" || fileExtension == ".xlsm")
                {
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                    using (var stream = System.IO.File.Open(selectedFilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                    {
                        using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
                        {
                            var result = reader.AsDataSet();
                            if (result.Tables.Count > 0)
                            {
                                var table = result.Tables[0];
                                int totalRows = table.Rows.Count;
                                int processedRows = 0;

                                for (int i = 1; i < totalRows; i++)
                                {
                                    var row = table.Rows[i];
                                    processedRows++;
                                    ProgressBar.Value = 10 + (processedRows * 80 / totalRows);

                                    if (row.ItemArray.All(cell => cell == null || string.IsNullOrWhiteSpace(cell.ToString())))
                                        continue;

                                    string rawDate = row[0]?.ToString() ?? "";
                                    string date = DateTime.TryParse(rawDate, out DateTime dt) ? dt.ToString("dd.MM.yyyy") : rawDate.Split(' ')[0];

                                    string category = row[9]?.ToString() ?? "";
                                    string rawAmount = row[4]?.ToString() ?? "";
                                    string description = row[11]?.ToString() ?? "";

                                    decimal amountValue = 0;
                                    string type = "Expense";
                                    if (decimal.TryParse(rawAmount, NumberStyles.Number, new CultureInfo("ru-RU"), out amountValue))
                                    {
                                        if (amountValue > 0)
                                        {
                                            type = "Income";
                                            rawAmount = "+" + amountValue.ToString("N2", new CultureInfo("ru-RU"));
                                        }
                                        else
                                        {
                                            rawAmount = amountValue.ToString("N2", new CultureInfo("ru-RU"));
                                        }
                                    }

                                    _transactions.Add(new TransactionRecord
                                    {
                                        Date = date,
                                        Category = category,
                                        Amount = rawAmount,
                                        Description = description,
                                        Balance = "",
                                        Type = type
                                    });
                                }
                            }
                            else
                            {
                                MessageBox.Show("Excel файл не содержит листов с данными.");
                            }
                        }
                    }
                }
                else if (fileExtension == ".ofx")
                {
                    _transactions = OfxParser.ParseOfx(selectedFilePath);
                }
                else
                {
                    MessageBox.Show("Неподдерживаемый формат файла.");
                    return;
                }

                ProgressBar.Value = 100;
                ProgressText.Text = "Обработка завершена.";
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обработке файла: {ex.Message}");
            }
            finally
            {
                TransactionsGrid.ItemsSource = _transactions;
                Debug.WriteLine($"Parsed rows: {_transactions.Count}");
                await Task.Delay(300);
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";
            }
        }

        // === КАТЕГОРИЗАЦИЯ (ML) ===
        private async void Categorize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_transactions == null || _transactions.Count == 0)
                {
                    MessageBox.Show("Нет данных для категоризации. Сначала загрузите/обработайте файл.");
                    return;
                }

                ProgressBar.Visibility = Visibility.Visible;
                ProgressText.Text = "Категоризация транзакций...";
                ProgressBar.Value = 10;

                if (sender is Button btn) btn.IsEnabled = false;

                var apiClient = _mlClient;

                var list = _transactions.ToList();
                list = await apiClient.CategorizeAsync(list);

                _transactions.Clear();
                foreach (var tr in list)
                {
                    tr.OriginalCategory = tr.Category;
                    _transactions.Add(tr);
                }

                ProgressBar.Value = 100;
                ProgressText.Text = "Категоризация завершена.";

                if (btnTrainModel != null)
                    btnTrainModel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при категоризации: " + ex.Message,
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                await Task.Delay(500);
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressBar.Value = 0;
                ProgressText.Text = string.Empty;

                if (sender is Button btn) btn.IsEnabled = true;
            }
        }

        private async void TeachModel_Click(object sender, RoutedEventArgs e)
        {
            if (_transactions == null || _transactions.Count == 0) return;


            var changedTransactions = _transactions
                .Where(t =>
                    !string.IsNullOrWhiteSpace(t.Description) &&
                    !string.IsNullOrWhiteSpace(t.Category) &&
                    t.Category != t.OriginalCategory)
                .ToList();

            if (changedTransactions.Count == 0)
            {
                MessageBox.Show("Вы не внесли изменений в предложенные категории.\n" +
                                "Дообучение необходимо, чтобы модель запомнила ваши исправления.",
                                "Нет изменений для обучения", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var res = MessageBox.Show(
                $"Вы исправили {changedTransactions.Count} записей.\n" +
                "Хотите обучить модель на этих исправлениях, чтобы она учитывала их в будущем?",
                "Дообучение модели",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                ProgressBar.Visibility = Visibility.Visible;
                ProgressText.Text = "Отправка данных на обучение...";
                if (sender is Button btn) btn.IsEnabled = false;

                var apiClient = new MlApiClient();
                bool success = await apiClient.SendFeedbackAsync(changedTransactions);

                if (success)
                {
                    foreach (var t in changedTransactions)
                    {
                        t.OriginalCategory = t.Category;
                    }

                    MessageBox.Show("Модель успешно дообучена! Спасибо за обратную связь.",
                                    "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Не удалось дообучить модель. Проверьте, запущен ли сервер ML.",
                                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                ProgressText.Text = "";
                if (sender is Button btn) btn.IsEnabled = true;
            }
        }

        private async void SaveDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (_transactions == null || _transactions.Count == 0)
            {
                MessageBox.Show("Нет данных для сохранения.");
                return;
            }

            var hasChanges = _transactions.Any(t => t.Category != t.OriginalCategory && !string.IsNullOrEmpty(t.OriginalCategory));
            if (hasChanges)
            {
                var trainRes = MessageBox.Show("Обнаружены исправленные категории. Хотите дообучить модель перед сохранением в базу?",
                    "Совет", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (trainRes == MessageBoxResult.Yes)
                {
                    TeachModel_Click(btnTrainModel, e);
                }
            }

            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressText.Text = "Сохранение транзакций...";

            await Task.Run(() =>
            {
                using (var db = new DatabaseManager())
                {
                    var connection = db.GetOpenConnection();
                    int userId;
                    using (var userIdCmd = new NpgsqlCommand("SELECT get_user_id(@username)", connection))
                    {
                        userIdCmd.Parameters.AddWithValue("@username", _username);
                        userId = Convert.ToInt32(userIdCmd.ExecuteScalar());
                    }

                    int total = _transactions.Count;
                    int current = 0;

                    foreach (var transaction in _transactions)
                    {
                        int categoryId;
                        using (var checkCmd = new NpgsqlCommand("SELECT id FROM categories WHERE name = @name LIMIT 1", connection))
                        {
                            checkCmd.Parameters.AddWithValue("@name", transaction.Category);
                            var result = checkCmd.ExecuteScalar();

                            if (result != null)
                            {
                                categoryId = Convert.ToInt32(result);
                            }
                            else
                            {
                                using (var insertCategoryCmd = new NpgsqlCommand(
                                    "INSERT INTO categories (name) VALUES (@name) RETURNING id",
                                    connection))
                                {
                                    insertCategoryCmd.Parameters.AddWithValue("@name", transaction.Category);
                                    categoryId = Convert.ToInt32(insertCategoryCmd.ExecuteScalar());
                                }
                            }
                        }

                        DateTime dt;
                        if (!DateTime.TryParseExact(transaction.Date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                        {
                            dt = DateTime.Now;
                        }

                        string rawAmount = transaction.Amount.Trim();
                        decimal amountValue = 0;
                        decimal.TryParse(rawAmount, NumberStyles.Number, new CultureInfo("ru-RU"), out amountValue);
                        string typeValue = transaction.Type;

                        using (var duplicateCheckCmd = new NpgsqlCommand(
     "SELECT id FROM transactions WHERE date=@date AND amount=@amount AND description=@description AND accountid=@accountid LIMIT 1", connection))
                        {
                            duplicateCheckCmd.Parameters.AddWithValue("@date", dt);
                            duplicateCheckCmd.Parameters.AddWithValue("@amount", amountValue);
                            duplicateCheckCmd.Parameters.AddWithValue("@description", transaction.Description ?? "");
                            duplicateCheckCmd.Parameters.AddWithValue("@accountid", _accountId);

                            var existingIdObj = duplicateCheckCmd.ExecuteScalar();

                            if (existingIdObj != null)
                            {

                                int existingId = Convert.ToInt32(existingIdObj);

                                using (var updateCmd = new NpgsqlCommand(
                                    "UPDATE transactions SET categoryid = @catId WHERE id = @id", connection))
                                {
                                    updateCmd.Parameters.AddWithValue("@catId", categoryId);
                                    updateCmd.Parameters.AddWithValue("@id", existingId);
                                    updateCmd.ExecuteNonQuery();
                                }

                                current++;
                                Application.Current.Dispatcher.Invoke(() => { ProgressBar.Value = (double)current / total * 100; });
                                continue;
                            }
                        }


                        // 3. Вставка
                        using (var insertTransactionCmd = new NpgsqlCommand(@"
                            INSERT INTO transactions 
                            (date, amount, type, categoryid, description, accountid)
                            VALUES 
                            (@date, @amount,@type::transaction_type, @categoryid, @description, @accountid)", connection))
                        {
                            insertTransactionCmd.Parameters.AddWithValue("@date", dt);
                            insertTransactionCmd.Parameters.AddWithValue("@amount", amountValue);
                            insertTransactionCmd.Parameters.AddWithValue("@type", typeValue);
                            insertTransactionCmd.Parameters.AddWithValue("@categoryid", categoryId);
                            insertTransactionCmd.Parameters.AddWithValue("@description", transaction.Description ?? "");
                            insertTransactionCmd.Parameters.AddWithValue("@accountid", _accountId);

                            insertTransactionCmd.ExecuteNonQuery();
                        }

                        current++;
                        Application.Current.Dispatcher.Invoke(() => { ProgressBar.Value = (double)current / total * 100; });
                    }
                }
            });

            ProgressText.Text = "Сохранение завершено!";
            await Task.Delay(500);
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Text = "";

            MessageBox.Show("Транзакции успешно сохранены в базу данных!");
        }

        private void ApplyFilter()
        {
            if (TransactionsGrid == null) return;
            if (TransactionsGrid.ItemsSource is ObservableCollection<TransactionRecord> originalList)
            {
                var filteredList = originalList.AsEnumerable();

                // Фильтр категории
                if (CategoryComboBox.SelectedItem is ComboBoxItem selectedCategoryItem)
                {
                    string selectedCategory = selectedCategoryItem.Content.ToString();
                    if (selectedCategory != "Все категории")
                    {
                        filteredList = filteredList.Where(r => r.Category == selectedCategory);
                    }
                }

                // Фильтр дат
                if (StartDatePicker.SelectedDate.HasValue)
                {
                    filteredList = filteredList.Where(r =>
                    {
                        if (DateTime.TryParse(r.Date, out DateTime date))
                            return date >= StartDatePicker.SelectedDate.Value;
                        return false;
                    });
                }

                if (EndDatePicker.SelectedDate.HasValue)
                {
                    filteredList = filteredList.Where(r =>
                    {
                        if (DateTime.TryParse(r.Date, out DateTime date))
                            return date <= EndDatePicker.SelectedDate.Value;
                        return false;
                    });
                }

                TransactionsGrid.ItemsSource = new ObservableCollection<TransactionRecord>(filteredList);
            }
        }

        private void FilterDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

        private void Button_Click_2(object sender, RoutedEventArgs e) // Очистить
        {
            if (TransactionsGrid != null)
            {
                _transactions.Clear();
                TransactionsGrid.ItemsSource = _transactions;
                if (btnTrainModel != null) btnTrainModel.Visibility = Visibility.Collapsed;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => this.Close();

        private async void ExportToOfx_Click(object sender, RoutedEventArgs e)
        {
            if (!(TransactionsGrid.ItemsSource is ObservableCollection<TransactionRecord> transactions && transactions.Count != 0))
            {
                MessageBox.Show("Нет данных для экспорта.");
                return;
            }

            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressText.Text = "Преобразование в OFX...";

            await Task.Run(() =>
            {
                OfxExporter.ExportToFile(transactions, (current, total) =>
                {
                    double progress = (double)current / total * 100;
                    Application.Current.Dispatcher.Invoke(() => { ProgressBar.Value = progress; });
                });
            });

            ProgressText.Text = "Файл OFX успешно создан!";
            await Task.Delay(800);
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Text = "";
        }

        private string DetectBank(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Unknown";

            string normalizedText = text.ToLower();
            int sberScore = 0;
            int tinkoffScore = 0;
            int ozonScore = 0;

            if (normalizedText.Contains("www.sberbank.ru")) sberScore += 5;
            if (normalizedText.Contains("пао сбербанк")) sberScore++;
            if (normalizedText.Contains("выписка по счёту дебетовой карты")) sberScore++;
            if (normalizedText.Contains("ул. вавилова, д. 19, москва")) sberScore++;

            if (normalizedText.Contains("tbank.ru")) tinkoffScore += 5;
            if (normalizedText.Contains("акционерное общество «тбанк»")) tinkoffScore++;
            if (normalizedText.Contains("2-я хуторская")) tinkoffScore++;
            if (normalizedText.Contains("справка о движении средств")) tinkoffScore++;

            if (normalizedText.Contains("ооо «озон банк»")) ozonScore += 5;
            if (normalizedText.Contains("пресненская набережная, дом 10")) ozonScore++;
            if (normalizedText.Contains("лицензия банка россии № 3542")) ozonScore++;

            if (sberScore > tinkoffScore && sberScore > ozonScore) return "Sber";
            if (tinkoffScore > sberScore && tinkoffScore > ozonScore) return "Tinkoff";
            if (ozonScore > sberScore && ozonScore > tinkoffScore) return "Ozon";

            return "Unknown";
        }

        private async Task<ReceiptParseResult> RecognizeReceiptAsync(string receiptPath)
        {
            ValidateReceiptFile(receiptPath);

            var mistralResult = await TryRecognizeReceiptWithMistralAsync(receiptPath);
            if (mistralResult != null && mistralResult.Records.Count > 0)
            {
                return mistralResult;
            }

            string recognizedText = await RunReceiptOcrAsync(receiptPath);
            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                throw new InvalidOperationException("OCR не вернул текст для выбранного изображения.");
            }

            var parseResult = ParseReceiptText(recognizedText);
            if (parseResult.Records.Count == 0)
            {
                throw new InvalidOperationException("Из текста чека не удалось извлечь позиции или итоговую сумму.");
            }

            return parseResult;
        }

        private async Task<ReceiptParseResult> TryRecognizeReceiptWithMistralAsync(string receiptPath)
        {
            try
            {
                var mistralService = new MistralService(new HttpClient());
                var receipt = await mistralService.RecognizeReceiptAsync(receiptPath);
                return MapMistralReceiptToParseResult(receipt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Mistral receipt recognition failed: " + ex.Message);
                return null;
            }
        }

        private ReceiptParseResult MapMistralReceiptToParseResult(ReceiptRecognitionResult receipt)
        {
            if (receipt == null)
            {
                return null;
            }

            string purchaseDate = NormalizeReceiptDate(receipt.PurchaseDate);
            string storeName = string.IsNullOrWhiteSpace(receipt.StoreName) ? "Чек" : receipt.StoreName.Trim();
            decimal? totalAmount = receipt.TotalAmount > 0 ? receipt.TotalAmount : (decimal?)null;

            var parseResult = new ReceiptParseResult
            {
                StoreName = storeName,
                PurchaseDate = purchaseDate,
                TotalAmount = totalAmount,
                Records = new List<TransactionRecord>()
            };

            foreach (var item in receipt.Items ?? Enumerable.Empty<ReceiptRecognitionItem>())
            {
                if (item == null || item.Amount <= 0)
                {
                    continue;
                }

                string resolvedItemName = ResolveReceiptItemName(receipt, item, totalAmount);
                if (string.IsNullOrWhiteSpace(resolvedItemName))
                {
                    continue;
                }

                parseResult.Records.Add(new TransactionRecord
                {
                    Date = purchaseDate,
                    Category = ReceiptCategoryName,
                    Amount = FormatExpenseAmount(item.Amount),
                    Balance = string.Empty,
                    Description = BuildReceiptDescription(storeName, resolvedItemName),
                    Type = "Expense"
                });
            }

            if (parseResult.Records.Count == 0 && totalAmount.HasValue)
            {
                parseResult.Records.Add(new TransactionRecord
                {
                    Date = purchaseDate,
                    Category = ReceiptCategoryName,
                    Amount = FormatExpenseAmount(totalAmount.Value),
                    Balance = string.Empty,
                    Description = BuildReceiptDescription(storeName, "Итог по чеку"),
                    Type = "Expense"
                });
            }

            return parseResult;
        }

        private string ResolveReceiptItemName(ReceiptRecognitionResult receipt, ReceiptRecognitionItem item, decimal? totalAmount)
        {
            string candidate = CleanupReceiptItemName(item.Name);
            if (IsUsefulReceiptItemName(candidate))
            {
                return candidate;
            }

            var lines = GetReceiptItemSection(ExtractReceiptLinesFromMarkdown(receipt.RawMarkdown));
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (!TryExtractAmountFromLine(line, out decimal lineAmount) || !AreAmountsClose(lineAmount, item.Amount))
                {
                    continue;
                }

                if (i > 0)
                {
                    string previousLineName = CleanupReceiptItemName(lines[i - 1]);
                    if (IsUsefulReceiptItemName(previousLineName))
                    {
                        return previousLineName;
                    }
                }

                if (i + 1 < lines.Count)
                {
                    string nextLineName = CleanupReceiptItemName(lines[i + 1]);
                    if (IsUsefulReceiptItemName(nextLineName) && !TryExtractAmountFromLine(lines[i + 1], out decimal nextAmount))
                    {
                        return nextLineName;
                    }
                }

                if (i > 1)
                {
                    string previousLineName = CleanupReceiptItemName(lines[i - 2]);
                    if (IsUsefulReceiptItemName(previousLineName))
                    {
                        return previousLineName;
                    }
                }
            }

            foreach (var line in lines)
            {
                if (!TryExtractAmountFromLine(line, out decimal lineAmount) || !AreAmountsClose(lineAmount, item.Amount))
                {
                    continue;
                }

                string inlineName = CleanupReceiptNameFromAmountLine(line, item.Amount);
                if (IsUsefulReceiptItemName(inlineName))
                {
                    return inlineName;
                }
            }

            if (totalAmount.HasValue && AreAmountsClose(item.Amount, totalAmount.Value))
            {
                return "Итог по чеку";
            }

            return null;
        }

        private void ValidateReceiptFile(string receiptPath)
        {
            if (string.IsNullOrWhiteSpace(receiptPath) || !File.Exists(receiptPath))
            {
                throw new FileNotFoundException("Файл чека не найден.");
            }

            string extension = Path.GetExtension(receiptPath)?.ToLowerInvariant();
            if (!SupportedReceiptExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Поддерживаются только изображения в форматах JPG, JPEG и PNG.");
            }
        }

        private async Task<string> RunReceiptOcrAsync(string receiptPath)
        {
            string tesseractPath = ResolveTesseractExecutablePath();
            if (string.IsNullOrWhiteSpace(tesseractPath))
            {
                throw new InvalidOperationException(
                    "Не найден Tesseract OCR. Установите Tesseract и языковые пакеты rus/eng, либо положите tesseract.exe рядом с приложением.");
            }

            string tempBasePath = Path.Combine(Path.GetTempPath(), "finacc_receipt_" + Guid.NewGuid().ToString("N"));
            string arguments = string.Format(
                CultureInfo.InvariantCulture,
                "\"{0}\" \"{1}\" -l {2} --psm 6",
                receiptPath,
                tempBasePath,
                string.Join("+", ReceiptOcrLanguages));

            var startInfo = new ProcessStartInfo
            {
                FileName = tesseractPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                string standardOutput = await process.StandardOutput.ReadToEndAsync();
                string standardError = await process.StandardError.ReadToEndAsync();
                await Task.Run(() => process.WaitForExit());

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError);
                }
            }

            string outputFile = tempBasePath + ".txt";
            try
            {
                return File.Exists(outputFile) ? File.ReadAllText(outputFile, Encoding.UTF8) : string.Empty;
            }
            finally
            {
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
        }

        private string ResolveTesseractExecutablePath()
        {
            var candidates = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tesseract.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tesseract", "tesseract.exe"),
                @"C:\Program Files\Tesseract-OCR\tesseract.exe",
                @"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"
            };

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var pathPart in pathEnv.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    candidates.Add(Path.Combine(pathPart.Trim(), "tesseract.exe"));
                }
                catch
                {
                    // Пропускаем некорректные пути из PATH.
                }
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private ReceiptParseResult ParseReceiptText(string rawText)
        {
            var lines = rawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeReceiptLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var itemSectionLines = GetReceiptItemSection(lines);
            var result = new ReceiptParseResult
            {
                StoreName = ExtractStoreName(lines),
                PurchaseDate = ExtractReceiptDate(lines),
                TotalAmount = ExtractTotalAmount(lines),
                Records = new List<TransactionRecord>()
            };

            var itemCandidates = ExtractReceiptItems(itemSectionLines, result.TotalAmount);
            foreach (var item in itemCandidates)
            {
                result.Records.Add(new TransactionRecord
                {
                    Date = result.PurchaseDate,
                    Category = ReceiptCategoryName,
                    Amount = FormatExpenseAmount(item.Amount),
                    Balance = string.Empty,
                    Description = BuildReceiptDescription(result.StoreName, item.Name),
                    Type = "Expense"
                });
            }

            if (result.Records.Count == 0 && result.TotalAmount.HasValue)
            {
                result.Records.Add(new TransactionRecord
                {
                    Date = result.PurchaseDate,
                    Category = ReceiptCategoryName,
                    Amount = FormatExpenseAmount(result.TotalAmount.Value),
                    Balance = string.Empty,
                    Description = BuildReceiptDescription(result.StoreName, "Итог по чеку"),
                    Type = "Expense"
                });
            }

            return result;
        }

        private List<string> GetReceiptItemSection(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return new List<string>();
            }

            int startIndex = 0;
            int headerIndex = lines.FindIndex(line =>
                line.IndexOf("КОЛ-ВО", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ИТОГО", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("ЦЕНА", StringComparison.OrdinalIgnoreCase) >= 0);

            if (headerIndex >= 0)
            {
                startIndex = headerIndex + 1;
            }

            int barcodeIndex = lines.FindIndex(line => Regex.IsMatch(line, @"^[\|\!Il1]{20,}$"));
            if (barcodeIndex >= 0 && barcodeIndex + 1 > startIndex)
            {
                startIndex = barcodeIndex + 1;
            }

            int endIndex = lines.FindIndex(startIndex, line => IsReceiptSummaryLine(line));
            if (endIndex < 0)
            {
                endIndex = lines.Count;
            }

            return lines.Skip(startIndex).Take(endIndex - startIndex).ToList();
        }

        private List<ReceiptItem> ExtractReceiptItems(List<string> lines, decimal? totalAmount)
        {
            var items = new List<ReceiptItem>();
            int index = 0;

            while (index < lines.Count)
            {
                string line = lines[index];
                if (ShouldSkipReceiptLine(line))
                {
                    index++;
                    continue;
                }

                if (TryParseInlineReceiptItem(line, totalAmount, out ReceiptItem inlineItem))
                {
                    items.Add(inlineItem);
                    index++;
                    continue;
                }

                if (index + 1 < lines.Count &&
                    TryParseSplitReceiptItem(line, lines[index + 1], totalAmount, out ReceiptItem splitItem))
                {
                    items.Add(splitItem);
                    index += 2;
                    continue;
                }

                index++;
            }

            return items;
        }

        private bool TryParseInlineReceiptItem(string line, decimal? totalAmount, out ReceiptItem item)
        {
            item = null;
            if (!TryExtractAmountFromLine(line, out decimal amount) || !IsValidReceiptAmount(amount, totalAmount))
            {
                return false;
            }

            string amountToken = ExtractLastAmountToken(line);
            if (string.IsNullOrWhiteSpace(amountToken))
            {
                return false;
            }

            string name = line.Substring(0, line.LastIndexOf(amountToken, StringComparison.Ordinal)).Trim(' ', '.', '-', ':', ';');
            name = Regex.Replace(name, @"\b\d+[xх*]\d+([,\.]\d+)?\b", string.Empty, RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b\d+([,\.]\d+)?\s?(кг|г|л|мл|шт)\b", string.Empty, RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\b\d+([,\.]\d{1,3})?\b", string.Empty, RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s{2,}", " ").Trim();
            name = CleanupReceiptItemName(name);

            if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            {
                return false;
            }

            item = new ReceiptItem
            {
                Name = name,
                Amount = amount
            };

            return true;
        }

        private bool TryParseSplitReceiptItem(string nameLine, string amountLine, decimal? totalAmount, out ReceiptItem item)
        {
            item = null;
            if (ShouldSkipReceiptLine(nameLine) || ShouldSkipReceiptLine(amountLine))
            {
                return false;
            }

            string cleanedName = CleanupReceiptItemName(nameLine);
            if (string.IsNullOrWhiteSpace(cleanedName) || !ContainsLetters(cleanedName) || cleanedName.StartsWith("НДС", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var amountMatches = Regex.Matches(amountLine, @"\d+[.,]\d{2,3}");
            if (amountMatches.Count == 0)
            {
                return false;
            }

            string totalToken = amountMatches[amountMatches.Count - 1].Value;
            if (!TryParseReceiptDecimal(totalToken, out decimal total) || !IsValidReceiptAmount(total, totalAmount))
            {
                return false;
            }

            item = new ReceiptItem
            {
                Name = cleanedName,
                Amount = total
            };

            return true;
        }

        private bool TryExtractAmountFromLine(string line, out decimal amount)
        {
            amount = 0;
            string token = ExtractLastAmountToken(line);
            return !string.IsNullOrWhiteSpace(token) && TryParseReceiptDecimal(token, out amount);
        }

        private string ExtractLastAmountToken(string line)
        {
            var matches = Regex.Matches(line, @"(?<!\d)(\d{1,3}(?:[ \.,]\d{3})*[.,]\d{2}|\d+)(?!\d)");
            if (matches.Count == 0)
            {
                return null;
            }

            return matches[matches.Count - 1].Value;
        }

        private string ExtractStoreName(List<string> lines)
        {
            return lines.FirstOrDefault(line =>
                line.Length >= 3 &&
                ContainsLetters(line) &&
                !line.Any(char.IsDigit) &&
                !ShouldSkipReceiptLine(line) &&
                line.IndexOf("КАССОВЫЙ ЧЕК", StringComparison.OrdinalIgnoreCase) < 0)
                ?? "Чек";
        }

        private string ExtractReceiptDate(List<string> lines)
        {
            var dateMatch = lines
                .Select(line => Regex.Match(line, @"(?<date>\d{1,2}[./-]\d{1,2}[./-]\d{2,4})(?:\s+(?<time>\d{1,2}:\d{2}(?::\d{2})?))?"))
                .FirstOrDefault(match => match.Success);

            if (dateMatch != null && dateMatch.Success)
            {
                string dateValue = dateMatch.Groups["date"].Value.Replace('-', '.').Replace('/', '.');
                string[] formats = { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy" };

                if (DateTime.TryParseExact(dateValue, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    return parsedDate.ToString("dd.MM.yyyy");
                }
            }

            return DateTime.Now.ToString("dd.MM.yyyy");
        }

        private string NormalizeReceiptDate(string rawDate)
        {
            if (string.IsNullOrWhiteSpace(rawDate))
            {
                return DateTime.Now.ToString("dd.MM.yyyy");
            }

            string normalized = rawDate.Trim().Replace('-', '.').Replace('/', '.');
            string[] formats =
            {
                "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy",
                "yyyy.MM.dd", "yyyy.M.d", "yyyy-MM-dd", "yyyy/MM/dd"
            };

            if (DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate.ToString("dd.MM.yyyy");
            }

            if (DateTime.TryParse(normalized, out parsedDate))
            {
                return parsedDate.ToString("dd.MM.yyyy");
            }

            return DateTime.Now.ToString("dd.MM.yyyy");
        }

        private decimal? ExtractTotalAmount(List<string> lines)
        {
            var totalLine = lines.LastOrDefault(line =>
                line.IndexOf("ИТОГ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ИТОГО", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ВСЕГО", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("ПОДЫТОГ", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!string.IsNullOrWhiteSpace(totalLine) && TryExtractAmountFromLine(totalLine, out decimal totalAmount))
            {
                return totalAmount;
            }

            var fallbackAmounts = lines
                .Where(line => TryExtractAmountFromLine(line, out _))
                .Select(line =>
                {
                    TryExtractAmountFromLine(line, out decimal value);
                    return value;
                })
                .Where(value => value > 0)
                .ToList();

            return fallbackAmounts.Count > 0 ? fallbackAmounts.Max() : (decimal?)null;
        }

        private bool IsReceiptSummaryLine(string line)
        {
            return ReceiptSummaryTokens.Any(token => line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string NormalizeReceiptLine(string line)
        {
            string normalized = (line ?? string.Empty).Trim();
            normalized = normalized.Replace('\t', ' ');
            normalized = normalized.Replace("|", " ");
            normalized = normalized.Replace("’", "'");
            normalized = normalized.Replace("`", "'");
            normalized = Regex.Replace(normalized, @"\s{2,}", " ");
            return normalized.Trim();
        }

        private bool TryParseReceiptDecimal(string value, out decimal amount)
        {
            amount = 0;
            string normalized = (value ?? string.Empty).Trim().Replace(" ", string.Empty);

            int lastComma = normalized.LastIndexOf(',');
            int lastDot = normalized.LastIndexOf('.');
            int separatorIndex = Math.Max(lastComma, lastDot);

            if (separatorIndex >= 0)
            {
                string integerPart = Regex.Replace(normalized.Substring(0, separatorIndex), @"[^\d-]", string.Empty);
                string fractionPart = Regex.Replace(normalized.Substring(separatorIndex + 1), @"[^\d]", string.Empty);
                normalized = integerPart + "." + fractionPart;
            }
            else
            {
                normalized = Regex.Replace(normalized, @"[^\d-]", string.Empty);
            }

            return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
        }

        private string FormatExpenseAmount(decimal amount)
        {
            return (-Math.Abs(amount)).ToString("N2", new CultureInfo("ru-RU"));
        }

        private string BuildReceiptDescription(string storeName, string itemName)
        {
            if (string.IsNullOrWhiteSpace(storeName))
            {
                return itemName;
            }

            if (string.IsNullOrWhiteSpace(itemName))
            {
                return storeName;
            }

            return storeName + ": " + itemName;
        }

        private List<string> ExtractReceiptLinesFromMarkdown(string markdown)
        {
            return (markdown ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeReceiptLine)
                .Select(line => Regex.Replace(line, @"[*_`#>\-\[\]\(\)\|]+", " ").Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private string CleanupReceiptNameFromAmountLine(string line, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            string amountPattern = Regex.Escape(amount.ToString("0.00", CultureInfo.InvariantCulture)).Replace("\\.", "[\\.,]");
            var amountMatch = Regex.Match(line, amountPattern);
            string namePart = amountMatch.Success
                ? line.Substring(0, amountMatch.Index)
                : line;

            if (!ContainsLetters(namePart))
            {
                return null;
            }

            namePart = Regex.Replace(namePart, @"\b\d+[xх*]\d+([,\.]\d+)?\b", string.Empty, RegexOptions.IgnoreCase);
            namePart = Regex.Replace(namePart, @"\b\d+([,\.]\d+)?\s?(кг|г|л|мл|шт|уп|упк)\b", string.Empty, RegexOptions.IgnoreCase);
            namePart = Regex.Replace(namePart, @"\b\d+([,\.]\d{1,3})?\b", string.Empty, RegexOptions.IgnoreCase);
            return CleanupReceiptItemName(namePart);
        }

        private bool IsUsefulReceiptItemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string cleaned = CleanupReceiptItemName(value);
            if (cleaned.Length < 3 || !ContainsLetters(cleaned))
            {
                return false;
            }

            if (ShouldSkipReceiptLine(cleaned))
            {
                return false;
            }

            if (Regex.IsMatch(cleaned, @"^\d+$"))
            {
                return false;
            }

            int letterCount = cleaned.Count(char.IsLetter);
            int digitCount = cleaned.Count(char.IsDigit);
            if (digitCount > letterCount)
            {
                return false;
            }

            return true;
        }

        private bool AreAmountsClose(decimal left, decimal right)
        {
            return Math.Abs(left - right) <= 0.02m;
        }

        private bool ShouldSkipReceiptLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            if (IsReceiptSummaryLine(line))
            {
                return true;
            }

            if (ReceiptServiceTokens.Any(token => line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            if (Regex.IsMatch(line, @"^[\d\W_]+$"))
            {
                return true;
            }

            if (line.StartsWith("НДС", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool IsValidReceiptAmount(decimal amount, decimal? totalAmount)
        {
            if (amount <= 0 || amount > 1000000m)
            {
                return false;
            }

            if (totalAmount.HasValue && totalAmount.Value > 0 && amount > totalAmount.Value * 1.2m)
            {
                return false;
            }

            return true;
        }

        private string CleanupReceiptItemName(string name)
        {
            string cleaned = (name ?? string.Empty).Trim();
            cleaned = Regex.Replace(cleaned, @"^[\*\#\@\+\-]+", string.Empty);
            cleaned = Regex.Replace(cleaned, @"\b(цена|скидка|кол-во|итого|кассовый чек)\b", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', '.', ',', ';', ':', '-', '*');
            return cleaned;
        }

        private bool ContainsLetters(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Any(char.IsLetter);
        }

        private void AppendReceiptToTransactions(ReceiptParseResult parseResult)
        {
            if (_transactions == null)
            {
                _transactions = new ObservableCollection<TransactionRecord>();
            }

            foreach (var record in parseResult.Records)
            {
                _transactions.Add(record);
            }

            TransactionsGrid.ItemsSource = _transactions;
        }

        private void SetBusyState(string progressText, double progressValue)
        {
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = progressValue;
            ProgressText.Text = progressText;
        }

        private void ResetBusyState()
        {
            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressBar.Value = 0;
            ProgressText.Text = string.Empty;
        }

        private sealed class ReceiptParseResult
        {
            public string StoreName { get; set; }
            public string PurchaseDate { get; set; }
            public decimal? TotalAmount { get; set; }
            public List<TransactionRecord> Records { get; set; }
        }

        private sealed class ReceiptItem
        {
            public string Name { get; set; }
            public decimal Amount { get; set; }
        }
    }
}
