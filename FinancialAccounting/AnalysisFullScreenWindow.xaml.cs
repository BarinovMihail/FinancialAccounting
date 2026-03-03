using System.Windows;

namespace FinancialAccounting
{
    public partial class AnalysisFullScreenWindow : Window
    {
        public AnalysisFullScreenWindow(string text)
        {
            InitializeComponent();
            AnalysisTextBox.Text = text ?? "";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
