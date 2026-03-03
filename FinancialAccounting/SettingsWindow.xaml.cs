using Npgsql;
using System;
using System.Windows;
using System.Windows.Controls;

namespace FinancialAccounting
{
    /// <summary>
    /// Логика взаимодействия для SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private string _username;
        private int _userId;

        public SettingsWindow(string username)
        {
            InitializeComponent();
            _username = username;
            _userId = GetUserIdByUsername(username);

            LoginTextBox.Text = _username;
            EmailTextBox.Text = GetEmailByLogin(_username);
            LoadAccounts();
        }

        private void LoadAccounts()
        {
            AccountsListBox.Items.Clear();

            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();

                // accounts.userid -> banks.bankname через bankid
                using (var command = new NpgsqlCommand(@"
                    SELECT a.id, b.bankname, a.accountnumber
                    FROM accounts a
                    JOIN banks b ON a.bankid = b.id
                    WHERE a.userid = @userid", connection))
                {
                    command.Parameters.AddWithValue("userid", _userId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            AccountsListBox.Items.Add(new Account
                            {
                                Id = reader.GetInt32(0),
                                BankName = reader.GetString(1),
                                AccountNumber = reader.GetString(2)
                            });
                        }
                    }
                }
            }
        }

        private string GetEmailByLogin(string username)
        {
            string email = string.Empty;

            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();

                using (var command = new NpgsqlCommand("SELECT get_email_by_login(@login)", connection))
                {
                    command.Parameters.AddWithValue("login", username);

                    var result = command.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        email = result.ToString();
                }
            }

            return email;
        }

        private bool VerifyOldPassword(string username, string oldPassword)
        {
            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();

                using (var command = new NpgsqlCommand("SELECT passwordhash FROM users WHERE login = @login", connection))
                {
                    command.Parameters.AddWithValue("login", username);

                    var result = command.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        string encryptedPassword = result.ToString();
                        string decryptedPassword = PasswordEncryptor.Decrypt(encryptedPassword);
                        return decryptedPassword == oldPassword;
                    }
                }
            }

            return false;
        }

        private void UpdatePassword(string username, string newPassword)
        {
            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();

                string encryptedPassword = PasswordEncryptor.Encrypt(newPassword);

                using (var command = new NpgsqlCommand(
                    "UPDATE users SET passwordhash = @passwordhash WHERE login = @login", connection))
                {
                    command.Parameters.AddWithValue("passwordhash", encryptedPassword);
                    command.Parameters.AddWithValue("login", username);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string oldPassword = OldPasswordBox.Password;
            string newPassword = NewPasswordBox.Password;
            string repeatPassword = RepeatPasswordBox.Password;

            if (newPassword != repeatPassword)
            {
                MessageBox.Show("Новый пароль и его подтверждение не совпадают.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!VerifyOldPassword(_username, oldPassword))
            {
                MessageBox.Show("Старый пароль введён неверно.",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            UpdatePassword(_username, newPassword);
            MessageBox.Show("Пароль успешно изменён. Пожалуйста, войдите снова.",
                "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = new MainWindow(_username);
            mainWindow.Show();
            this.Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            var loginWindow = new LoginWindow();
            loginWindow.Show();
            this.Close();
        }

        private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is Account selectedAccount)
            {
                BankNameTextBox.Text = selectedAccount.BankName;
                AccountNumberTextBox.Text = selectedAccount.AccountNumber;
            }
        }

        private void SaveAccountChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (AccountsListBox.SelectedItem is Account selectedAccount)
            {
                string newBankName = BankNameTextBox.Text;
                string newAccountNumber = AccountNumberTextBox.Text;

                using (var dbManager = new DatabaseManager())
                {
                    var connection = dbManager.GetOpenConnection();

                    int bankId;

                    // Ищем банк
                    using (var bankCmd = new NpgsqlCommand(
                        "SELECT id FROM banks WHERE bankname = @name LIMIT 1", connection))
                    {
                        bankCmd.Parameters.AddWithValue("name", newBankName);
                        var result = bankCmd.ExecuteScalar();

                        if (result != null && result != DBNull.Value)
                        {
                            bankId = Convert.ToInt32(result);
                        }
                        else
                        {
                            // Создаем банк
                            using (var insertBankCmd = new NpgsqlCommand(
                                "INSERT INTO banks (bankname) VALUES (@name) RETURNING id", connection))
                            {
                                insertBankCmd.Parameters.AddWithValue("name", newBankName);
                                bankId = Convert.ToInt32(insertBankCmd.ExecuteScalar());
                            }
                        }
                    }

                    // Обновляем счёт
                    using (var command = new NpgsqlCommand(
                        "UPDATE accounts SET bankid = @bankid, accountnumber = @accountnumber WHERE id = @id",
                        connection))
                    {
                        command.Parameters.AddWithValue("bankid", bankId);
                        command.Parameters.AddWithValue("accountnumber", newAccountNumber);
                        command.Parameters.AddWithValue("id", selectedAccount.Id);
                        command.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("Изменения сохранены.", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                LoadAccounts();
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl &&
                ((TabControl)sender).SelectedItem is TabItem selectedTab &&
                selectedTab.Header.ToString() == "Счета")
            {
                LoadAccounts();
            }
        }

        private int GetUserIdByUsername(string username)
        {
            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();

                using (var command = new NpgsqlCommand(
                    "SELECT get_user_id(@username)", connection))
                {
                    command.Parameters.AddWithValue("username", username);

                    var result = command.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        return Convert.ToInt32(result);
                }
            }

            throw new Exception("Пользователь не найден.");
        }
    }

    public class Account
    {
        public int Id { get; set; }
        public string BankName { get; set; }
        public string AccountNumber { get; set; }

        public override string ToString() => $"{BankName} ({AccountNumber})";
    }
}
