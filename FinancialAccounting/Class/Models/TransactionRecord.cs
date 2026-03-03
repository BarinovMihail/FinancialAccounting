using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinancialAccounting.Class.Models
{
    public class TransactionRecord : INotifyPropertyChanged
    {
        public string Date { get; set; }
        public string Amount { get; set; }
        public string Description { get; set; }
        public string Balance { get; set; }
        public string Type { get; set; }
        private string _category;
        public string Category
        {
            get { return _category; }
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged(nameof(Category));
                }
            }
        }

        public string OriginalCategory { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}