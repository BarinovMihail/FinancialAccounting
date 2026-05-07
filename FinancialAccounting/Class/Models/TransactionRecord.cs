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
        private double _mlConfidence;
        private string _predictionSource;
        private bool _needsReview;
        private string _suggestedCategory;
        private string _suggestionReason;
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

        public double MlConfidence
        {
            get { return _mlConfidence; }
            set
            {
                if (System.Math.Abs(_mlConfidence - value) > 0.0001)
                {
                    _mlConfidence = value;
                    OnPropertyChanged(nameof(MlConfidence));
                    OnPropertyChanged(nameof(MlConfidenceDisplay));
                }
            }
        }

        public string MlConfidenceDisplay
        {
            get { return MlConfidence > 0 ? MlConfidence.ToString("P0") : string.Empty; }
        }

        public string PredictionSource
        {
            get { return _predictionSource; }
            set
            {
                if (_predictionSource != value)
                {
                    _predictionSource = value;
                    OnPropertyChanged(nameof(PredictionSource));
                }
            }
        }

        public bool NeedsReview
        {
            get { return _needsReview; }
            set
            {
                if (_needsReview != value)
                {
                    _needsReview = value;
                    OnPropertyChanged(nameof(NeedsReview));
                }
            }
        }

        public string SuggestedCategory
        {
            get { return _suggestedCategory; }
            set
            {
                if (_suggestedCategory != value)
                {
                    _suggestedCategory = value;
                    OnPropertyChanged(nameof(SuggestedCategory));
                }
            }
        }

        public string SuggestionReason
        {
            get { return _suggestionReason; }
            set
            {
                if (_suggestionReason != value)
                {
                    _suggestionReason = value;
                    OnPropertyChanged(nameof(SuggestionReason));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
