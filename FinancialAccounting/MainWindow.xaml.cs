using LiveCharts;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Убрал дублирующийся using
using System.Globalization;
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

namespace FinancialAccounting
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string _username;
        public MainWindow(string username)
        {
            InitializeComponent();
            _username = username;
            UsernameTextBlock.Text = $"Пользователь: {_username}";
            LoadAccounts(_username);

        }

        private void LoadCharts(int accountId)
        {
            var expenseData = new Dictionary<string, double>();
            var incomeData = new Dictionary<string, double>();

            using (var db = new DatabaseManager())
            {
                var conn = db.GetOpenConnection();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                    SELECT COALESCE(c.name, 'Без категории') AS category,
                     t.type,
                     SUM(t.amount) AS total_amount
                     FROM transactions t
                        LEFT JOIN categories c ON t.categoryid = c.id
                        WHERE t.accountid = @accountId
                        AND LOWER(COALESCE(c.name, '')) NOT LIKE 'тест%'
                        GROUP BY COALESCE(c.name, 'Без категории'), t.type;
                            ";
                    cmd.Parameters.AddWithValue("accountId", accountId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string category = reader["category"]?.ToString() ?? "Без категории";
                            string type = reader["type"]?.ToString()?.ToLower() ?? "";
                            double amount = Convert.ToDouble(reader["total_amount"]);

                            // Если расходы хранятся отрицательными — приводим к положительным
                            if (type == "expense") amount = Math.Abs(amount);

                            if (type == "expense")
                                expenseData[category] = expenseData.TryGetValue(category, out var v) ? v + amount : amount;
                            else if (type == "income")
                                incomeData[category] = incomeData.TryGetValue(category, out var v) ? v + amount : amount;
                        }
                    }
                }
            }

            BuildTop5Pie(ExpenseChart, expenseData);
            BuildTop5Pie(IncomeChart, incomeData);
        }

        private void BuildTop5Pie(LiveCharts.Wpf.PieChart chart, Dictionary<string, double> data)
        {
            chart.Series = new SeriesCollection();
            if (data == null || data.Count == 0) return;

            double total = data.Values.Sum();
            if (total <= 0) return;

            // настройки
            const int topN = 6;              // можно 5-7
            const int minPercentToShow = 3;  // <-- порог (2 или 3 обычно лучше всего)

            // LabelPoint лучше через Participation
            Func<ChartPoint, string> labelPoint = cp => cp.Participation.ToString("P0", CultureInfo.InvariantCulture);

            bool Show(double value)
            {
                var percent = (value / total) * 100.0;
                var rounded = (int)Math.Round(percent, 0, MidpointRounding.AwayFromZero);
                return rounded >= minPercentToShow;
            }

            var top = data.OrderByDescending(x => x.Value).Take(topN).ToList();
            double topSum = top.Sum(x => x.Value);

            // "прочее" = всё, что не попало в topN + (опционально) то, что ниже порога
            // Чтобы избежать ситуации, когда topN набрали много мелочи, можно сначала отделить "мелочь" по порогу:
            var big = top.Where(x => Show(x.Value)).ToList();
            double bigSum = big.Sum(x => x.Value);

            // Сумма мелких из topN + оставшиеся категории
            double otherSum = Math.Max(0, total - bigSum);

            foreach (var kvp in big)
            {
                chart.Series.Add(new PieSeries
                {
                    Title = kvp.Key,
                    Values = new ChartValues<double> { kvp.Value },
                    DataLabels = true,
                    LabelPoint = labelPoint,
                    Foreground = Brushes.Black,
                    FontSize = 14
                });
            }

            if (otherSum > 0 && Show(otherSum))
            {
                chart.Series.Add(new PieSeries
                {
                    Title = "Прочее",
                    Values = new ChartValues<double> { otherSum },
                    DataLabels = true,
                    LabelPoint = labelPoint,
                    Foreground = Brushes.Black,
                    FontSize = 14
                });
            }
        }



        private void LoadAccounts(string username)
        {
            try
            {
                List<AccountInfo> accounts = new List<AccountInfo>();

                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    // ОБНОВЛЕННЫЙ SQL ЗАПРОС:
                    // Вместо чтения accounts.bankname, делаем JOIN с таблицей banks
                    cmd.CommandText = @"
                        SELECT a.id, b.bankname, a.accountnumber
                        FROM accounts a
                        JOIN banks b ON a.bankid = b.id
                        WHERE a.userid = get_user_id(@username)";

                    cmd.Parameters.AddWithValue("username", username);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string bankName = reader.GetString(1); // Теперь это берется из таблицы banks
                            string accountNumber = reader.GetString(2);
                            string last4 = accountNumber.Length >= 4 ? accountNumber.Substring(accountNumber.Length - 4) : "XXXX";

                            accounts.Add(new AccountInfo
                            {
                                Id = reader.GetInt32(0),
                                DisplayName = $"{bankName} ••••{last4}"
                            });
                        }
                    }
                }

                AccountComboBox.ItemsSource = accounts;
                AccountComboBox.DisplayMemberPath = "DisplayName";
                AccountComboBox.SelectedValuePath = "Id";

                if (accounts.Count > 0)
                    AccountComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке счетов: " + ex.Message);
            }
        }


        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            // Передаем имя пользователя, чтобы создать счет для него
            var addAccountWindow = new AddAccountWindow(_username);

            // Подписываемся на закрытие окна добавления, чтобы обновить список счетов
            addAccountWindow.Closed += (s, args) => LoadAccounts(_username);

            addAccountWindow.ShowDialog(); // Используем ShowDialog, чтобы блокировать главное окно
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (AccountComboBox.SelectedItem is AccountInfo selectedAccount)
            {
                int accountId = selectedAccount.Id;

                var uploadWindow = new DataUploadWindow(accountId, _username);
                uploadWindow.ShowDialog();
                // После закрытия окна загрузки графики могут измениться, стоит их обновить
                LoadCharts(accountId);
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите счёт.");
            }
        }

        private void AccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountComboBox.SelectedItem is AccountInfo selectedAccount)
            {
                int userId = GetUserIdByUsername(_username); // Это можно оптимизировать, но пока оставим
                int accountId = selectedAccount.Id;

                if (userId > 0 && accountId > 0)
                {
                    LoadCharts(accountId);
                }
                else
                {
                    MessageBox.Show("Ошибка при определении пользователя или счёта.");
                }
            }
        }

        private int GetUserIdByUsername(string username)
        {
            try
            {
                using (var db = new DatabaseManager())
                using (var cmd = db.GetOpenConnection().CreateCommand())
                {
                    cmd.CommandText = "SELECT get_user_id(@username)";
                    cmd.Parameters.AddWithValue("username", username);

                    object result = cmd.ExecuteScalar();
                    if (result != null && int.TryParse(result.ToString(), out int userId))
                    {
                        return userId;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при получении userId: " + ex.Message);
            }

            return -1; // Возврат -1 если не удалось получить userId
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (AccountComboBox.SelectedItem is AccountInfo selectedAccount)
            {
                int userId = GetUserIdByUsername(_username);
                int accountId = selectedAccount.Id;
                var reportsWindow = new ReportsWindow(accountId, userId);
                reportsWindow.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите счёт.");
            }

        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            if (AccountComboBox.SelectedItem is AccountInfo selectedAccount)
            {
                int accountId = selectedAccount.Id;
                var analyticsWindow = new AnalyticsWindow(accountId);
                analyticsWindow.Show();
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите счёт.");
            }
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_username);
            settingsWindow.Show();
            this.Close();
        }
    }
}

public class AccountInfo
{
    public int Id { get; set; }
    public string DisplayName { get; set; } // Название для ComboBox
}
