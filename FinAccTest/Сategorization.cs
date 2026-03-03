using FinancialAccounting.Class.Models;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinAccTest
{
    public class MlApiClientTests
    {
        [Fact]
        public async Task CategorizeAsync_ShouldFillCategories_FromMlResponse()
        {
            // arrange
            var transactions = new List<TransactionRecord>
            {
                new TransactionRecord { Description = "Оплата в V NOVGOROD TPP MOSCOW RUS", Amount = "-100", Date = "01.01.2025", Category = "" },
                new TransactionRecord { Description = "Оплата в PYATEROCHKA 653 VEL.NOVGOROD RUS", Amount = "1000", Date = "02.01.2025", Category = "" }
            };

            // подготавливаем JSON-ответ от ML-сервиса
            var mlResponse = new MlPredictResponse
            {
                success = true,
                results = new List<MlPredictedTransaction>
                {
                    new MlPredictedTransaction { predicted_category = "Транспорт" },
                    new MlPredictedTransaction { predicted_category = "Супермаркеты" }
                }
            };

            string responseJson = JsonConvert.SerializeObject(mlResponse);

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(responseJson),
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var client = new MlApiClient(httpClient);

            // act
            var result = await client.CategorizeAsync(transactions);

            // assert
            Assert.Equal(2, result.Count);
            Assert.Equal("Транспорт", result[0].Category);
            Assert.Equal("Супермаркеты", result[1].Category);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri.ToString().Contains("/predict")),
                ItExpr.IsAny<CancellationToken>());
        }
    }
}
