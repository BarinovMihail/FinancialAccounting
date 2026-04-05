using Npgsql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FinancialAccounting
{
    public partial class BudgetManagementWindow : Window
    {
        public BudgetManagementWindow()
        {
            InitializeComponent();
            LoadCategories();
            LoadBudgets();
        }

        private void LoadCategories()
        {
            CategoryComboBox.Items.Clear();

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

            if (CategoryComboBox.Items.Count > 0)
                CategoryComboBox.SelectedIndex = 0;
        }

        private void LoadBudgets()
        {
            var budgets = new List<BudgetDisplayItem>();

            using (var dbManager = new DatabaseManager())
            {
                var connection = dbManager.GetOpenConnection();
                using (var command = new NpgsqlCommand(
                    @"SELECT cb.id, cb.category_id, c.name, cb.amount
                      FROM category_budgets cb
                      JOIN categories c ON cb.category_id = c.id
                      ORDER BY c.name", connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        budgets.Add(new BudgetDisplayItem
                        {
                            Id = reader.GetInt32(0),
                            CategoryId = reader.GetInt32(1),
                            CategoryName = reader.GetString(2),
                            Amount = reader.GetDecimal(3),
                            AmountText = reader.GetDecimal(3).ToString("N2")
                        });
                    }
                }
            }

            BudgetListView.ItemsSource = budgets;
        }

        private void SaveBudget_Click(object sender, RoutedEventArgs e)
        {
            var selectedCategory = CategoryComboBox.SelectedItem as ComboBoxItem;
            if (selectedCategory == null || selectedCategory.Tag == null)
            {
                MessageBox.Show("Выберите категорию.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string amountText = BudgetAmountBox.Text.Trim().Replace(',', '.');
            if (!decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount < 0)
            {
                MessageBox.Show("Введите корректную неотрицательную сумму бюджета.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int categoryId = (int)selectedCategory.Tag;

            try
            {
                using (var dbManager = new DatabaseManager())
                {
                    var connection = dbManager.GetOpenConnection();
                    using (var command = new NpgsqlCommand(
                        @"INSERT INTO category_budgets (category_id, amount)
                          VALUES (@categoryId, @amount)
                          ON CONFLICT (category_id) DO UPDATE
                          SET amount = EXCLUDED.amount, updated_at = NOW()", connection))
                    {
                        command.Parameters.AddWithValue("@categoryId", categoryId);
                        command.Parameters.AddWithValue("@amount", amount);
                        command.ExecuteNonQuery();
                    }
                }

                BudgetAmountBox.Clear();
                LoadBudgets();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при сохранении бюджета:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteBudget_Click(object sender, RoutedEventArgs e)
        {
            var selectedBudget = BudgetListView.SelectedItem as BudgetDisplayItem;
            if (selectedBudget == null)
            {
                MessageBox.Show("Выберите бюджет для удаления.", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Вы уверены, что хотите удалить бюджет для категории '{selectedBudget.CategoryName}'?",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using (var dbManager = new DatabaseManager())
                {
                    var connection = dbManager.GetOpenConnection();
                    using (var command = new NpgsqlCommand("DELETE FROM category_budgets WHERE id = @id", connection))
                    {
                        command.Parameters.AddWithValue("@id", selectedBudget.Id);
                        command.ExecuteNonQuery();
                    }
                }

                LoadBudgets();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при удалении бюджета:\n" + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    internal class BudgetDisplayItem
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public decimal Amount { get; set; }
        public string AmountText { get; set; }
    }
}
