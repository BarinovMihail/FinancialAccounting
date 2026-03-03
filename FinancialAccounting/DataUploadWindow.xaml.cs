using ExcelDataReader;
using FinancialAccounting.Class;
using FinancialAccounting.Class.Models;
using FinancialAccounting.Class.Parsers;
using Microsoft.Win32;
using Npgsql;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
    }
}
