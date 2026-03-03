using FinancialAccounting.Class;
using FinancialAccounting.Class.Models; // Подставь свой namespace, где лежит TransactionRecord
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public class MlApiClient : IMlApiClient
{
    private readonly HttpClient _httpClient;

    private readonly string _predictUrl = "http://127.0.0.1:8000/predict";
    private readonly string _feedbackUrl = "http://127.0.0.1:8000/feedback";

    public MlApiClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
    {
    }

    public MlApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }
    /// <summary>
    /// Отправляет транзакции на Python-сервер для предсказания категорий.
    /// </summary>
    public async Task<List<TransactionRecord>> CategorizeAsync(List<TransactionRecord> transactions)
    {
        if (transactions == null || transactions.Count == 0)
            return transactions;

        // Формируем тело запроса
        var request = new MlPredictRequest
        {
            transactions = transactions.Select(t => new MlTransaction
            {
                description = t.Description ?? string.Empty,
                amount = t.Amount ?? string.Empty,
                date = t.Date ?? string.Empty
            }).ToList()
        };

        // Сериализация
        string json = JsonConvert.SerializeObject(request, Formatting.None);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_predictUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                throw new Exception($"ML API Predict error: {response.StatusCode}\n{err}");
            }

            string respJson = await response.Content.ReadAsStringAsync();
            var mlResponse = JsonConvert.DeserializeObject<MlPredictResponse>(respJson);

            if (mlResponse == null || !mlResponse.success || mlResponse.results == null)
                return transactions;

            // Обновляем категории в исходном списке по порядку
            for (int i = 0; i < transactions.Count && i < mlResponse.results.Count; i++)
            {
                transactions[i].Category = mlResponse.results[i].predicted_category;
            }

            return transactions;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ML Predict error: " + ex);
            return transactions;
        }
    }

    /// <summary>
    /// </summary>
    public async Task<bool> SendFeedbackAsync(List<TransactionRecord> transactions)
    {
        if (transactions == null || transactions.Count == 0)
            return false;

        var feedbackItems = transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Description) && !string.IsNullOrWhiteSpace(t.Category))
            .Select(t => new FeedbackItemDto
            {
                Description = t.Description,
                CorrectCategory = t.Category
            })
            .ToList();

        if (feedbackItems.Count == 0)
            return false;

        var requestPayload = new FeedbackRequestDto { Items = feedbackItems };
        string json = JsonConvert.SerializeObject(requestPayload, Formatting.None);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_feedbackUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string err = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"ML Feedback error code: {response.StatusCode}\n{err}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ML Feedback error: " + ex);
            return false;
        }
    }


    private class FeedbackRequestDto
    {
        [JsonProperty("items")]
        public List<FeedbackItemDto> Items { get; set; }
    }

    private class FeedbackItemDto
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("correct_category")]
        public string CorrectCategory { get; set; }
    }
}
