using FinancialAccounting.Class;
using LiveCharts;
using LiveCharts.Wpf;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FinancialAccounting
{
    public partial class AnalyticsWindow : Window
    {
        private readonly int _accountId;
        private readonly List<TransactionPoint> data = new List<TransactionPoint>();
        private List<DailyPoint> _daily = new List<DailyPoint>();

        public AnalyticsWindow(int accountId)
        {
            InitializeComponent();
            _accountId = accountId;

            MainChart.DisableAnimations = true;
            Top5ExpensesChart.DisableAnimations = true;

            LoadCategories();
            InitDashboardEmpty();
        }

        private void InitDashboardEmpty()
        {
            MaxIncomeText.Text = "—";
            MaxIncomeDateText.Text = "";
            MaxExpenseText.Text = "—";
            MaxExpenseDateText.Text = "";
            TopMarketText.Text = "—";
            TopMarketSumText.Text = "";

            TopMarketsList.ItemsSource = null;

            Top5ExpensesChart.Series = new SeriesCollection();
            Top5ExpensesChart.AxisX = new AxesCollection { new Axis { Labels = Array.Empty<string>() } };
            Top5ExpensesChart.AxisY = new AxesCollection { new Axis { LabelFormatter = v => v.ToString("N0") } };
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

        private class ChartBucket
        {
            public string Label { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public decimal Sum { get; set; }
        }

        private List<ChartBucket> DownsampleDaily(List<DailyPoint> daily, int maxBars)
        {
            var result = new List<ChartBucket>();
            if (daily == null || daily.Count == 0) return result;

            if (daily.Count <= maxBars)
            {
                foreach (var d in daily)
                {
                    result.Add(new ChartBucket
                    {
                        StartDate = d.Date.Date,
                        EndDate = d.Date.Date,
                        Label = d.Date.ToString("dd.MM.yy"),
                        Sum = d.Sum
                    });
                }
                return result;
            }

            int groupSize = (int)Math.Ceiling(daily.Count / (double)maxBars);

            for (int i = 0; i < daily.Count; i += groupSize)
            {
                var chunk = daily.Skip(i).Take(groupSize).ToList();
                var start = chunk.First().Date.Date;
                var end = chunk.Last().Date.Date;

                result.Add(new ChartBucket
                {
                    StartDate = start,
                    EndDate = end,
                    Label = start == end ? start.ToString("dd.MM.yy") : $"{start:dd.MM.yy}-{end:dd.MM.yy}",
                    Sum = chunk.Sum(x => x.Sum)
                });
            }

            return result;
        }

        private void BuildChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string chartType = (ChartTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Гистограмма";
                string operationType = (TypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
                var selectedCategory = CategoryComboBox.SelectedItem as ComboBoxItem;

                DateTime? startDate = StartDatePicker.SelectedDate;
                DateTime? endDate = EndDatePicker.SelectedDate;

                string query = @"
SELECT
    t.date,
    t.amount,
    COALESCE(t.description, '') AS description,
    COALESCE(c.name, 'Без категории') AS category,
    t.categoryid,
    t.type::text AS type
FROM transactions t
LEFT JOIN categories c ON t.categoryid = c.id
WHERE t.accountid = @accountid
  AND LOWER(COALESCE(c.name, '')) NOT LIKE 'тест%'
";

                if (operationType != "Все")
                    query += " AND t.type = @type::transaction_type";
                if (selectedCategory?.Tag != null)
                    query += " AND t.categoryid = @categoryid";
                if (startDate.HasValue)
                    query += " AND t.date >= @startDate";
                if (endDate.HasValue)
                    query += " AND t.date <= @endDate";

                query += " ORDER BY t.date;";

                data.Clear();

                using (var dbManager = new DatabaseManager())
                {
                    var connection = dbManager.GetOpenConnection();
                    using (var command = new NpgsqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@accountid", _accountId);

                        if (operationType != "Все")
                            command.Parameters.AddWithValue("@type", operationType == "Доход" ? "Income" : "Expense");

                        if (selectedCategory?.Tag != null)
                            command.Parameters.AddWithValue("@categoryid", (int)selectedCategory.Tag);

                        if (startDate.HasValue)
                            command.Parameters.AddWithValue("@startDate", startDate.Value.Date);

                        if (endDate.HasValue)
                            command.Parameters.AddWithValue("@endDate", endDate.Value.Date.AddDays(1).AddTicks(-1));

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                data.Add(new TransactionPoint
                                {
                                    Date = reader.GetDateTime(0),
                                    Amount = reader.GetDecimal(1),      // расход отрицательный, доход положительный
                                    Description = reader.GetString(2),
                                    Category = reader.GetString(3),
                                    CategoryId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                                    Type = reader.GetString(5)          // "Income"/"Expense"
                                });
                            }
                        }
                    }
                }

                // Всегда обновляем дашборд по текущей выборке
                RefreshDashboard();

                // Очищаем графики
                MainChart.Series.Clear();
                MainChart.AxisX.Clear();
                MainChart.AxisY.Clear();
                PieChart.Series.Clear();

                if (data.Count == 0)
                {
                    MainChart.Visibility = Visibility.Visible;
                    PieChart.Visibility = Visibility.Collapsed;
                    SetupAxesByLabels(Array.Empty<string>());
                    return;
                }

                _daily = data
                    .GroupBy(t => t.Date.Date)
                    .Select(g => new DailyPoint { Date = g.Key, Sum = g.Sum(x => x.Amount) })
                    .OrderBy(x => x.Date)
                    .ToList();

                if (chartType == "Круговая диаграмма")
                {
                    MainChart.Visibility = Visibility.Collapsed;
                    PieChart.Visibility = Visibility.Visible;

                    var byCategory = data
                        .GroupBy(t => string.IsNullOrWhiteSpace(t.Category) ? "Без категории" : t.Category)
                        .ToDictionary(g => g.Key, g => (double)g.Sum(x => Math.Abs(x.Amount)));

                    BuildTop5Pie(PieChart, byCategory);
                    return;
                }

                MainChart.Visibility = Visibility.Visible;
                PieChart.Visibility = Visibility.Collapsed;

                var buckets = DownsampleDaily(_daily, 140);
                var values = new ChartValues<double>(buckets.Select(b => (double)b.Sum));
                var labels = buckets.Select(b => b.Label).ToArray();

                SetupAxesByLabels(labels);

                if (chartType == "Гистограмма")
                {
                    var series = new ColumnSeries
                    {
                        Title = "Сумма по дням",
                        Values = values,
                        DataLabels = false,
                        MaxColumnWidth = 30,
                        LabelPoint = p =>
                        {
                            int i = (int)p.X;
                            if (i < 0 || i >= buckets.Count) return "";
                            var b = buckets[i];
                            var dateText = b.StartDate == b.EndDate
                                ? b.StartDate.ToString("dd.MM.yyyy")
                                : $"{b.StartDate:dd.MM.yyyy}-{b.EndDate:dd.MM.yyyy}";
                            return $"{dateText}\nСумма: {b.Sum:N0}";
                        }
                    };
                    MainChart.Series.Add(series);
                }
                else // "Линейный график"
                {
                    var lineSeries = new LineSeries
                    {
                        Title = "Сумма по дням",
                        Values = values,
                        DataLabels = false,

                        StrokeThickness = 2,
                        Fill = Brushes.Transparent,

                        PointGeometry = DefaultGeometries.Circle,
                        PointGeometrySize = 8,
                        PointForeground = Brushes.White,

                        LabelPoint = p =>
                        {
                            int i = (int)p.X;
                            if (i < 0 || i >= buckets.Count) return "";
                            var b = buckets[i];
                            var dateText = b.StartDate == b.EndDate
                                ? b.StartDate.ToString("dd.MM.yyyy")
                                : $"{b.StartDate:dd.MM.yyyy}-{b.EndDate:dd.MM.yyyy}";
                            return $"{dateText}\nСумма: {b.Sum:N0}";
                        }
                    };
                    MainChart.Series.Add(lineSeries);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось построить график. Проверьте фильтры/данные.\n\n" + ex.Message,
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetupAxesByLabels(string[] labels)
        {
            MainChart.AxisX.Clear();
            MainChart.AxisY.Clear();

            int count = labels?.Length ?? 0;
            int step = 1;
            if (count > 0)
                step = Math.Max(1, (int)Math.Ceiling(count / 12.0));

            MainChart.AxisX.Add(new Axis
            {
                Title = "Дата",
                Labels = labels ?? Array.Empty<string>(),
                LabelsRotation = 15,
                Separator = new LiveCharts.Wpf.Separator { Step = step }
            });

            MainChart.AxisY.Add(new Axis
            {
                Title = "Сумма",
                LabelFormatter = v => v.ToString("N0")
            });
        }

        private void BuildTop5Pie(LiveCharts.Wpf.PieChart chart, Dictionary<string, double> dataDict)
        {
            chart.Series = new SeriesCollection();
            if (dataDict == null || dataDict.Count == 0) return;

            double total = dataDict.Values.Sum();
            if (total <= 0) return;

            var top5 = dataDict.OrderByDescending(x => x.Value).Take(5).ToList();
            double top5Sum = top5.Sum(x => x.Value);
            double otherSum = Math.Max(0, total - top5Sum);

            Func<LiveCharts.ChartPoint, string> labelPoint =
                cp => cp.Participation.ToString("P0", CultureInfo.InvariantCulture);

            Func<double, bool> show = value =>
            {
                var percent = (value / total) * 100.0;
                var rounded = (int)Math.Round(percent, 0, MidpointRounding.AwayFromZero);
                return rounded >= 2;
            };

            foreach (var kvp in top5)
            {
                if (!show(kvp.Value)) continue;

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

            if (otherSum > 0 && show(otherSum))
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

        private class MarketStat
        {
            public string Merchant { get; set; }
            public int Count { get; set; }
            public decimal Sum { get; set; }
            public string SumText => Sum.ToString("N0");
        }

        private void RefreshDashboard()
        {
            var incomes = data.Where(x => x.Type == "Income").ToList();
            var expenses = data.Where(x => x.Type == "Expense").ToList(); // Amount отрицательный

            var maxIncome = incomes.OrderByDescending(x => x.Amount).FirstOrDefault();
            MaxIncomeText.Text = maxIncome == null ? "—" : $"{maxIncome.Amount:N0}";
            MaxIncomeDateText.Text = maxIncome == null ? "" : $"{maxIncome.Date:dd.MM.yyyy}";

            // самый большой расход = самое отрицательное
            var maxExpense = expenses.OrderBy(x => x.Amount).FirstOrDefault();
            MaxExpenseText.Text = maxExpense == null ? "—" : $"{Math.Abs(maxExpense.Amount):N0}";
            MaxExpenseDateText.Text = maxExpense == null ? "" : $"{maxExpense.Date:dd.MM.yyyy}";

            // популярный супермаркет (categoryId=33) по количеству
            var marketAgg = expenses
                .Where(x => x.CategoryId == 33)
                .Select(x => new { Merchant = ExtractMerchant(x.Description), AmountAbs = Math.Abs(x.Amount) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Merchant))
                .GroupBy(x => x.Merchant)
                .Select(g => new { Merchant = g.Key, Count = g.Count(), Sum = g.Sum(z => z.AmountAbs) })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.Sum)
                .FirstOrDefault();

            TopMarketText.Text = marketAgg == null ? "—" : $"{marketAgg.Merchant} ({marketAgg.Count} чек.)";
            TopMarketSumText.Text = marketAgg == null ? "" : $"Потрачено: {marketAgg.Sum:N0}";

            // Top‑10 супермаркетов (список)
            var topMarkets10 = expenses
                .Where(x => x.CategoryId == 33)
                .Select(x => new { Merchant = ExtractMerchant(x.Description), AmountAbs = Math.Abs(x.Amount) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Merchant))
                .GroupBy(x => x.Merchant)
                .Select(g => new MarketStat
                {
                    Merchant = g.Key,
                    Count = g.Count(),
                    Sum = g.Sum(z => z.AmountAbs)
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.Sum)
                .Take(5)
                .ToList();

            TopMarketsList.ItemsSource = topMarkets10;

            // Top‑5 категорий расходов по сумме (abs)
            var top5 = expenses
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Category) ? "Без категории" : x.Category)
                .Select(g => new { Category = g.Key, Sum = g.Sum(t => Math.Abs(t.Amount)) })
                .OrderByDescending(x => x.Sum)
                .Take(5)
                .ToList();

            Top5ExpensesChart.Series = new SeriesCollection
            {
                new ColumnSeries
                {
                    Title = "Расходы",
                    Values = new ChartValues<double>(top5.Select(x => (double)x.Sum)),
                    DataLabels = true,
                    LabelPoint = p =>
                    {
                        int i = (int)p.X;
                        if (i < 0 || i >= top5.Count) return "";
                        return $"{top5[i].Category}\n{top5[i].Sum:N0}";
                    }
                }
            };

            Top5ExpensesChart.AxisX = new AxesCollection
            {
                new Axis
                {
                    Labels = top5.Select(x => x.Category).ToArray(),
                    LabelsRotation = 15
                }
            };

            Top5ExpensesChart.AxisY = new AxesCollection
            {
                new Axis { LabelFormatter = v => v.ToString("N0") }
            };
        }

        // Из "Оплата в PYATEROCHKA 22158 VEL.NOVGOROD RUS" -> "PYATEROCHKA"
        private string ExtractMerchant(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;

            var s = description.Trim();

            // 1. убираем "Оплата в "
            s = Regex.Replace(s, @"^\s*Оплата\s+в\s+", "", RegexOptions.IgnoreCase);

            // 2. отрезаем хвосты по городу/стране
            s = Regex.Replace(s, @"\s+(VEL\..*|RUS.*)$", "", RegexOptions.IgnoreCase);

            // 3. убираем номер магазина в конце
            s = Regex.Replace(s, @"\s+\d+\s*$", "", RegexOptions.IgnoreCase);

            // 4. нормализация пробелов
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();

            if (s.Length == 0) return null;

            var upper = s.ToUpperInvariant();

            // 5. Кастомные правила для сетей
            if (upper.StartsWith("MAGNIT"))
                return "MAGNIT";

            if (upper.StartsWith("PYATEROCHKA") || upper.StartsWith("ПЯТЕРОЧКА"))
                return "PYATEROCHKA";

            if (upper.StartsWith("LENTA"))
                return "LENTA";

            // Можно добавить и другие сети по вкусу:
            // if (upper.StartsWith("OSEN")) return "OSEN";
            // if (upper.StartsWith("KRASNOE&BELOE")) return "KRASNOE&BELOE";

            // 6. по умолчанию – возвращаем обрезанное имя как есть
            return s;
        }


        private async void GenerateAnalysis_Click(object sender, RoutedEventArgs e)
        {
            NeuroAnalysisText.Text = "Анализируем данные...";

            var summary = new StringBuilder();
            summary.AppendLine(string.Format("Период: {0} - {1}",
                StartDatePicker.SelectedDate.HasValue ? StartDatePicker.SelectedDate.Value.ToShortDateString() : "",
                EndDatePicker.SelectedDate.HasValue ? EndDatePicker.SelectedDate.Value.ToShortDateString() : ""
            ));
            summary.AppendLine(string.Format("Тип операций: {0}", (TypeComboBox.SelectedItem as ComboBoxItem)?.Content ?? "Все"));
            summary.AppendLine(string.Format("Категория: {0}", (CategoryComboBox.SelectedItem as ComboBoxItem)?.Content ?? "Все"));
            summary.AppendLine(string.Format("Количество транзакций: {0}", data.Count));
            summary.AppendLine(string.Format("Чистая сумма (доход-расход): {0:N0}", data.Sum(d => d.Amount)));
            summary.AppendLine(string.Format("Расходы (модуль): {0:N0}", data.Where(x => x.Type == "Expense").Sum(x => Math.Abs(x.Amount))));
            summary.AppendLine(string.Format("Доходы: {0:N0}", data.Where(x => x.Type == "Income").Sum(x => x.Amount)));

            if (data.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Примеры транзакций (до 10):");

                foreach (var t in data.Take(10))
                {
                    var desc = (t.Description ?? "").Trim();
                    if (desc.Length > 80) desc = desc.Substring(0, 80) + "...";
                    summary.AppendLine(string.Format("- {0:dd.MM.yyyy}: {1:N0} ({2})", t.Date, t.Amount, desc));
                }
            }

            try
            {
                var mistralService = new MistralService(new HttpClient());
                string analysis = await mistralService.GetAnalysisAsync(summary.ToString());
                NeuroAnalysisText.Text = analysis;
            }
            catch (Exception ex)
            {
                NeuroAnalysisText.Text = "Ошибка анализа: " + ex.Message;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl)
            {
                var selectedTab = tabControl.SelectedItem as TabItem;
                if (selectedTab != null && selectedTab.Header?.ToString() == "БЮДЖЕТЫ ПО КАТЕГОРИЯМ")
                {
                    LoadBudgetTab();
                }
            }
        }

        private void OpenBudgetManagement_Click(object sender, RoutedEventArgs e)
        {
            var win = new BudgetManagementWindow();
            win.Owner = this;
            win.ShowDialog();
            LoadBudgetTab();
        }

        private void LoadBudgetTab()
        {
            try
            {
                var categoryNames = new List<string>();
                var actualValues = new List<double>();
                var budgetValues = new List<double>();

                using (var dbManager = new DatabaseManager())
                {
                    var connection = dbManager.GetOpenConnection();
                    using (var command = new NpgsqlCommand(
                        @"SELECT c.name,
                                 COALESCE(ABS(SUM(t.amount)), 0) AS actual_expense,
                                 cb.amount AS budget_amount
                          FROM category_budgets cb
                          JOIN categories c ON cb.category_id = c.id
                          LEFT JOIN transactions t ON t.categoryid = c.id
                                                   AND t.accountid = @accountId
                                                   AND t.type = 'Expense'::transaction_type
                          GROUP BY c.name, cb.amount
                          ORDER BY c.name", connection))
                    {
                        command.Parameters.AddWithValue("@accountId", _accountId);

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                categoryNames.Add(reader.GetString(0));
                                actualValues.Add(reader.IsDBNull(1) ? 0 : (double)reader.GetDecimal(1));
                                budgetValues.Add((double)reader.GetDecimal(2));
                            }
                        }
                    }
                }

                // Разделяем фактические значения на две серии: в пределах бюджета и с превышением
                var normalValues = new List<double>();
                var overspendValues = new List<double>();
                var overspendCategories = new List<(string Name, double Actual, double Budget)>();

                for (int i = 0; i < categoryNames.Count; i++)
                {
                    if (actualValues[i] > budgetValues[i])
                    {
                        // Превышение — красная серия показывает значение, нормальная — 0
                        overspendValues.Add(actualValues[i]);
                        normalValues.Add(0);
                        overspendCategories.Add((categoryNames[i], actualValues[i], budgetValues[i]));
                    }
                    else
                    {
                        // В пределах бюджета — нормальная серия показывает значение, красная — 0
                        normalValues.Add(actualValues[i]);
                        overspendValues.Add(0);
                    }
                }

                BudgetComparisonChart.Series = new SeriesCollection
                {
                    new ColumnSeries
                    {
                        Title = "Факт (расходы)",
                        Values = new ChartValues<double>(normalValues),
                        DataLabels = true,
                        MaxColumnWidth = 40,
                        Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x98, 0xDB)),
                        LabelPoint = p => p.Y == 0 ? "" : p.Y.ToString("N0")
                    },
                    new ColumnSeries
                    {
                        Title = "Превышение",
                        Values = new ChartValues<double>(overspendValues),
                        DataLabels = true,
                        MaxColumnWidth = 40,
                        Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x00)),
                        LabelPoint = p => p.Y == 0 ? "" : p.Y.ToString("N0")
                    },
                    new ColumnSeries
                    {
                        Title = "Бюджет",
                        Values = new ChartValues<double>(budgetValues),
                        DataLabels = true,
                        MaxColumnWidth = 40,
                        LabelPoint = p => p.Y.ToString("N0")
                    }
                };

                BudgetComparisonChart.AxisX = new AxesCollection
                {
                    new Axis
                    {
                        Labels = categoryNames.ToArray(),
                        LabelsRotation = 15
                    }
                };

                BudgetComparisonChart.AxisY = new AxesCollection
                {
                    new Axis
                    {
                        Title = "Сумма",
                        LabelFormatter = v => v.ToString("N0")
                    }
                };

                // Показываем предупреждение о превышении бюджета
                if (overspendCategories.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Внимание! Превышение бюджета по следующим категориям:");
                    sb.AppendLine();
                    foreach (var item in overspendCategories)
                    {
                        double over = item.Actual - item.Budget;
                        sb.AppendLine($"• {item.Name}: потрачено {item.Actual:N2} ₽ из {item.Budget:N2} ₽ (превышение на {over:N2} ₽)");
                    }
                    MessageBox.Show(sb.ToString(), "Превышение бюджета",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки данных бюджета:\n" + ex.Message,
                                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenAnalysisFullScreen_Click(object sender, RoutedEventArgs e)
        {
            var win = new AnalysisFullScreenWindow(NeuroAnalysisText.Text);
            win.Owner = this;
            win.Show();
        }
    }

    public class TransactionPoint
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }        // расход отрицательный, доход положительный
        public string Description { get; set; }
        public string Category { get; set; }
        public int? CategoryId { get; set; }
        public string Type { get; set; }           // "Expense" / "Income"
    }

    internal class DailyPoint
    {
        public DateTime Date { get; set; }
        public decimal Sum { get; set; }
    }
}
