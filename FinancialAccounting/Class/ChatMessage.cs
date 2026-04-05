using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FinancialAccounting.Class
{
    public class ChatMessage : INotifyPropertyChanged
    {
        public string Role { get; set; }   // "user" or "assistant"

        private string _text;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                OnPropertyChanged();
            }
        }

        public DateTime CreatedAt { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
