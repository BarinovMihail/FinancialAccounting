using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Npgsql; // Не забудь добавить этот using

namespace FinancialAccounting
{
    public partial class AddAccountWindow : Window
    {
        private string _username;

        public AddAccountWindow(string username)
        {
            InitializeComponent();
            _username = username;
        }

        private void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            string cardNumber = CardNumberBox.Text.Trim();
            string bankName = BankNameBox.Text.Trim();

            if (string.IsNullOrEmpty(cardNumber) || string.IsNullOrEmpty(bankName))
            {
                MessageBox.Show("Пожалуйста, заполните все поля.");
                return;
            }

            try
            {
                int userId;
                int bankId;

                using (var db = new DatabaseManager())
                {
                    using (var conn = db.GetOpenConnection())
                    {
                        // 1. Получаем ID пользователя через функцию
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT get_user_id(@username)";
                            cmd.Parameters.AddWithValue("username", _username);

                            object result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                MessageBox.Show("Не удалось получить ID пользователя.");
                                return;
                            }
                            userId = Convert.ToInt32(result);
                        }

                        // 2. Получаем ID банка (или создаем новый, если такого нет)
                        // Сначала пробуем найти
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT id FROM banks WHERE bankname = @bankname LIMIT 1";
                            cmd.Parameters.AddWithValue("bankname", bankName);
                            object result = cmd.ExecuteScalar();

                            if (result != null && result != DBNull.Value)
                            {
                                bankId = Convert.ToInt32(result);
                            }
                            else
                            {
                                // Если банка нет, создаем его
                                cmd.CommandText = "INSERT INTO banks (bankname) VALUES (@bankname) RETURNING id";
                                // Параметр bankname уже добавлен выше, но для чистоты можно очистить и добавить заново или просто выполнить, т.к. имя параметра то же
                                bankId = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                        }

                        // 3. Добавляем счет
                        using (var cmd = conn.CreateCommand())
                        {
                            // ВАЖНО: В твоей новой схеме в accounts есть поле bankid, а не bankname
                            cmd.CommandText = @"
                                INSERT INTO accounts (userid, bankid, accountnumber)
                                VALUES (@userid, @bankid, @accountnumber);
                            ";

                            cmd.Parameters.AddWithValue("userid", userId);
                            cmd.Parameters.AddWithValue("bankid", bankId); // Передаем ID банка
                            cmd.Parameters.AddWithValue("accountnumber", cardNumber);

                            cmd.ExecuteNonQuery();
                        }
                    }
                }

                MessageBox.Show("Счет успешно добавлен!");

                // Переоткрываем главное окно, чтобы обновить список счетов
                this.DialogResult = true;
                this.Close();
              
            }
            catch (PostgresException pex)
            {
                // Обработка ошибок Postgres (например, дубликат номера карты)
                if (pex.SqlState == "23505") // Unique constraint violation
                {
                    MessageBox.Show("Счет с таким номером уже существует.");
                }
                else
                {
                    MessageBox.Show("Ошибка базы данных: " + pex.Message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при добавлении счета: " + ex.Message);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CardNumberBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text, 0);
        }

        private void CardNumberBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Логика форматирования номера карты (пробелы каждые 4 цифры)
            CardNumberBox.TextChanged -= CardNumberBox_TextChanged;

            string text = CardNumberBox.Text.Replace(" ", "");

            // Ограничим длину, например, 16 или 20 цифр, чтобы не было переполнения
            if (text.Length > 20) text = text.Substring(0, 20);

            StringBuilder formattedText = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    formattedText.Append(" ");
                }
                formattedText.Append(text[i]);
            }

            CardNumberBox.Text = formattedText.ToString();
            CardNumberBox.CaretIndex = CardNumberBox.Text.Length;

            CardNumberBox.TextChanged += CardNumberBox_TextChanged;
        }
    }
}
