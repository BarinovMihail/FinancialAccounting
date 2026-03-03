using Xunit;
using System.Collections.ObjectModel;
using FinancialAccounting;
namespace FinAccTest
{
    public class PdfParserTests
    {
        [Fact]
        public void ParsePdfText_ParsesTransactionsCorrectly()
        {
            string pdfText = @"
Выписка по счёту дебетовой картыСтраница 3 из 8ДАТА ОПЕРАЦИИ (МСК)Дата обработки? и код авторизацииКАТЕГОРИЯОписание операцииСУММА В ВАЛЮТЕ СЧЁТАСумма в валюте операции?ОСТАТОК СРЕДСТВ В ВАЛЮТЕ СЧЁТА27.12.202413:46981314Прочие операции407,0010 125,04
";     
            var result = SberbankPdfParser.ParsePdfText(pdfText);

            Assert.Single(result);

            Assert.Equal("27.12.2024", result[0].Date);
            Assert.Equal("Прочие операции", result[0].Category);
            Assert.Equal("-407,00", result[0].Amount);
            Assert.Equal("10 125,04", result[0].Balance);
        }
    }
}