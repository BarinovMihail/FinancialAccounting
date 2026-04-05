using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FinancialAccounting.Class
{
    public class MistralService
    {
        private const string ApiKey = "WdNK0AwaJg27oRiLv67gd9Ztao8jcAt6";
        private const string ChatApiUrl = "https://api.mistral.ai/v1/chat/completions";
        private const string OcrApiUrl = "https://api.mistral.ai/v1/ocr";
        private readonly HttpClient _httpClient;

        public MistralService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
            }
        }

        public async Task<string> GetAnalysisAsync(string inputData)
        {
            var requestBody = new
            {
                model = "mistral-medium",
                messages = new[]
                {
                    new { role = "system", content = "You are a financial analyst. Analyze the data and provide a short conclusion and a short recommendation in Russian." },
                    new { role = "user", content = inputData }
                },
                max_tokens = 2000,
                temperature = 0.7
            };

            return await SendChatRequestAsync(requestBody);
        }

        public async Task<string> GetChatResponseAsync(string fullPrompt)
        {
            string systemContent = "";
            string userContent = fullPrompt;

            const string sysMarker = "|||SYSTEM|||";
            const string userMarker = "|||USER|||";

            if (fullPrompt.StartsWith(sysMarker))
            {
                var parts = fullPrompt.Split(new[] { userMarker }, StringSplitOptions.None);
                systemContent = parts[0].Replace(sysMarker, "").Trim();
                userContent = parts.Length > 1 ? parts[1].Trim() : "";
            }

            var messages = new List<object>();
            if (!string.IsNullOrEmpty(systemContent))
                messages.Add(new { role = "system", content = systemContent });
            messages.Add(new { role = "user", content = userContent });

            var requestBody = new
            {
                model = "mistral-medium",
                messages,
                max_tokens = 2000,
                temperature = 0.7
            };

            return await SendChatRequestAsync(requestBody);
        }

        public async Task<ReceiptRecognitionResult> RecognizeReceiptAsync(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                throw new FileNotFoundException("Receipt image was not found.", imagePath);
            }

            string preparedImagePath = null;

            try
            {
                preparedImagePath = PrepareReceiptImage(imagePath);
                string imageDataUrl = BuildImageDataUrl(preparedImagePath);
                string markdown = await RunReceiptOcrAsync(imageDataUrl);

                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new Exception("Mistral OCR returned an empty result.");
                }

                var structuredReceipt = await StructureReceiptFromMarkdownAsync(markdown);
                structuredReceipt.RawMarkdown = markdown;
                structuredReceipt.Items = (structuredReceipt.Items ?? new List<ReceiptRecognitionItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name) && item.Amount > 0)
                    .ToList();

                return structuredReceipt;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(preparedImagePath) &&
                    !string.Equals(preparedImagePath, imagePath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(preparedImagePath))
                {
                    File.Delete(preparedImagePath);
                }
            }
        }

        private async Task<string> RunReceiptOcrAsync(string imageDataUrl)
        {
            var requestBody = new
            {
                model = "mistral-ocr-latest",
                document = new
                {
                    type = "image_url",
                    image_url = imageDataUrl
                },
                include_image_base64 = false
            };

            var requestContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(OcrApiUrl, requestContent);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Mistral OCR error: {responseJson}");
            }

            var ocrResult = JsonSerializer.Deserialize<MistralOcrResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                (ocrResult?.Pages ?? new List<MistralOcrPage>())
                    .Select(page => page.Markdown)
                    .Where(markdown => !string.IsNullOrWhiteSpace(markdown)));
        }

        private async Task<ReceiptRecognitionResult> StructureReceiptFromMarkdownAsync(string markdown)
        {
            var requestBody = new
            {
                model = "mistral-small-latest",
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "Extract structured data from OCR text of a retail receipt. Return JSON only. " +
                            "Keep only purchased items. Ignore VAT/NDS lines, discounts, change, payment details, tax lines, QR info, cashier info, address, INN, KKT, OFD, ads and bonus or promo lines. " +
                            "Use format: {\"store_name\":\"\",\"purchase_date\":\"dd.MM.yyyy or empty\",\"total_amount\":0,\"items\":[{\"name\":\"\",\"amount\":0}]}. " +
                            "Do not invent values. If you are unsure, leave an empty string or 0."
                    },
                    new
                    {
                        role = "user",
                        content =
                            "Parse this OCR receipt text into JSON. " +
                            "Use the final line total for total_amount. " +
                            "For each item, amount must be the full amount for the line item, not unit price. " +
                            "Exclude items with zero amount." +
                            "\n\nOCR text:\n" + markdown
                    }
                },
                temperature = 0.1,
                max_tokens = 3000
            };

            string responseText = await SendChatRequestAsync(requestBody);
            var receipt = JsonSerializer.Deserialize<ReceiptRecognitionResult>(responseText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (receipt == null)
            {
                throw new Exception("Could not parse structured receipt JSON.");
            }

            return receipt;
        }

        private async Task<string> SendChatRequestAsync(object requestBody)
        {
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(ChatApiUrl, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Mistral API Error: {responseJson}");
            }

            var result = JsonSerializer.Deserialize<MistralChatResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? string.Empty;
        }

        private string PrepareReceiptImage(string imagePath)
        {
            using (var sourceImage = Image.FromFile(imagePath))
            {
                int width = Math.Max(sourceImage.Width * 2, sourceImage.Width);
                int height = Math.Max(sourceImage.Height * 2, sourceImage.Height);
                var outputPath = Path.Combine(Path.GetTempPath(), "finacc_receipt_prepared_" + Guid.NewGuid().ToString("N") + ".png");

                using (var preparedBitmap = new Bitmap(width, height))
                using (var graphics = Graphics.FromImage(preparedBitmap))
                using (var attributes = new ImageAttributes())
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.White);

                    var colorMatrix = new ColorMatrix(new[]
                    {
                        new[] { 1.35f, 0f, 0f, 0f, 0f },
                        new[] { 0f, 1.35f, 0f, 0f, 0f },
                        new[] { 0f, 0f, 1.35f, 0f, 0f },
                        new[] { 0f, 0f, 0f, 1f, 0f },
                        new[] { -0.08f, -0.08f, -0.08f, 0f, 1f }
                    });

                    attributes.SetColorMatrix(colorMatrix);
                    graphics.DrawImage(
                        sourceImage,
                        new Rectangle(0, 0, width, height),
                        0,
                        0,
                        sourceImage.Width,
                        sourceImage.Height,
                        GraphicsUnit.Pixel,
                        attributes);

                    ApplyThreshold(preparedBitmap, 185);
                    preparedBitmap.Save(outputPath, ImageFormat.Png);
                }

                return outputPath;
            }
        }

        private void ApplyThreshold(Bitmap bitmap, byte threshold)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    byte gray = (byte)((pixel.R * 0.299) + (pixel.G * 0.587) + (pixel.B * 0.114));
                    byte binary = gray >= threshold ? (byte)255 : (byte)0;
                    bitmap.SetPixel(x, y, Color.FromArgb(binary, binary, binary));
                }
            }
        }

        private string BuildImageDataUrl(string imagePath)
        {
            string extension = Path.GetExtension(imagePath)?.ToLowerInvariant();
            string mimeType;

            switch (extension)
            {
                case ".png":
                    mimeType = "image/png";
                    break;
                case ".jpg":
                case ".jpeg":
                    mimeType = "image/jpeg";
                    break;
                default:
                    mimeType = "application/octet-stream";
                    break;
            }

            byte[] bytes = File.ReadAllBytes(imagePath);
            string base64 = Convert.ToBase64String(bytes);
            return $"data:{mimeType};base64,{base64}";
        }
    }

    public class MistralChatResponse
    {
        [JsonPropertyName("choices")]
        public List<MistralChatChoice> Choices { get; set; }
    }

    public class MistralChatChoice
    {
        [JsonPropertyName("message")]
        public MistralChatMessage Message { get; set; }
    }

    public class MistralChatMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    public class MistralOcrResponse
    {
        [JsonPropertyName("pages")]
        public List<MistralOcrPage> Pages { get; set; }
    }

    public class MistralOcrPage
    {
        [JsonPropertyName("markdown")]
        public string Markdown { get; set; }
    }

    public class ReceiptRecognitionResult
    {
        [JsonPropertyName("store_name")]
        public string StoreName { get; set; }

        [JsonPropertyName("purchase_date")]
        public string PurchaseDate { get; set; }

        [JsonPropertyName("total_amount")]
        public decimal TotalAmount { get; set; }

        [JsonPropertyName("items")]
        public List<ReceiptRecognitionItem> Items { get; set; }

        [JsonIgnore]
        public string RawMarkdown { get; set; }
    }

    public class ReceiptRecognitionItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }
    }
}
