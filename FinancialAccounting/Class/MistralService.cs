using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FinancialAccounting.Class
{
    public class MistralService
    {
        private const string apiKey = "WdNK0AwaJg27oRiLv67gd9Ztao8jcAt6";
        private const string apiUrl = "https://api.mistral.ai/v1/chat/completions";
        private readonly HttpClient _httpClient;

        public MistralService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<string> GetAnalysisAsync(string inputData)
        {
            var requestBody = new
            {
                model = "mistral-medium",
                messages = new[]
                {
                    new { role = "system", content = "Ты финансовый аналитик. Проанализируй данные, дай краткий вывод и краткую рекомендацию по оптимизации расходов(если есть данные о расходах) на русском." },
                    new { role = "user", content = inputData }
                },
                max_tokens = 2000,
                temperature = 0.7
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Mistral API Error: {error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<MistralResponse>(responseJson);

            return result?.choices?.FirstOrDefault()?.message?.content?.Trim() ?? "Анализ не удался";
        }
    }

    public class MistralResponse
    {
        public List<Choice> choices { get; set; }

        public class Choice
        {
            public Message message { get; set; }
        }

        public class Message
        {
            public string content { get; set; }
        }
    }
}