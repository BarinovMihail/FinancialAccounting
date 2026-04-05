using System;

namespace FinancialAccounting.Class
{
    public class ChatMessage
    {
        public string Role { get; set; }       // "user" or "assistant"
        public string Text { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
