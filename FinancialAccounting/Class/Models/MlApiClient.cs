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
    private readonly string _enrichWebUrl = "http://127.0.0.1:8000/enrich-web";

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
                date = t.Date ?? string.Empty,
                type = t.Type ?? string.Empty
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
                var prediction = mlResponse.results[i];
                var firstSuggestion = prediction.suggestions?.FirstOrDefault();

                transactions[i].Category = prediction.predicted_category;
                transactions[i].MlConfidence = prediction.confidence;
                transactions[i].PredictionSource = prediction.source;
                transactions[i].NeedsReview = prediction.needs_review;
                transactions[i].SuggestedCategory = firstSuggestion?.category;
                transactions[i].SuggestionReason = firstSuggestion?.reason ?? prediction.suggestion_reason;
            }

            return transactions;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ML Predict error: " + ex);
            return transactions;
        }
    }

    public async Task<MlCategorySuggestion> EnrichWebAsync(TransactionRecord transaction, List<string> availableCategories)
    {
        if (transaction == null || string.IsNullOrWhiteSpace(transaction.Description))
            return null;

        var requestPayload = new EnrichWebRequestDto
        {
            Description = transaction.Description ?? string.Empty,
            Amount = transaction.Amount ?? string.Empty,
            AvailableCategories = availableCategories ?? new List<string>()
        };

        string json = JsonConvert.SerializeObject(requestPayload, Formatting.None);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_enrichWebUrl, content);
            string respJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"ML EnrichWeb error code: {response.StatusCode}\n{respJson}");
                return null;
            }

            var enrichResponse = JsonConvert.DeserializeObject<EnrichWebResponseDto>(respJson);
            if (enrichResponse == null || !enrichResponse.Success || enrichResponse.Suggestion == null)
            {
                System.Diagnostics.Debug.WriteLine("ML EnrichWeb rejected: " + (enrichResponse?.Message ?? respJson));
                return null;
            }

            return enrichResponse.Suggestion;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ML EnrichWeb error: " + ex);
            return null;
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

    private class EnrichWebRequestDto
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("amount")]
        public string Amount { get; set; }

        [JsonProperty("available_categories")]
        public List<string> AvailableCategories { get; set; }
    }

    private class EnrichWebResponseDto
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("suggestion")]
        public MlCategorySuggestion Suggestion { get; set; }

        [JsonProperty("safe_query")]
        public string SafeQuery { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
