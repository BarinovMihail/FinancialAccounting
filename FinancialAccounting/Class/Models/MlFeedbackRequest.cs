using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FinancialAccounting 
{
    public class MlFeedbackRequest
    {
        [JsonPropertyName("items")]
        public List<MlFeedbackItem> Items { get; set; } = new List<MlFeedbackItem>();
    }

    public class MlFeedbackItem
    {
        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("correct_category")]
        public string CorrectCategory { get; set; }
    }

    public class MlFeedbackResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("updated_count")]
        public int UpdatedCount { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
