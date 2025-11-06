using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CRYPTOAnalize.Services
{
    public class AI_Responser
    {
        public static async Task<string> ASK(string userMessage)
        {
            var response = await GetResponseFromNewAPI(userMessage);
            return response;
        }

        private static async Task<string> GetResponseFromNewAPI(string userMessage)
        {
            string mod = "deepseek-r1-0528";

            string apiKey = "sk-aitunnel-IP8Sl5Rci8d636Cf3rn5sTR2mrS7GluP";
            string baseURL = "https://api.aitunnel.ru/v1/";

            // Конфигурация HTTP клиента
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(4); // <-- КЛЮЧЕВАЯ СТРОКА
                httpClient.BaseAddress = new Uri(baseURL);
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                // Данные для запроса
                var requestBody = new
                {
                    messages = new[]
                    {
                    new { role = "user", content = userMessage }
                },
                    max_tokens = 35000,
                    model = mod
                };

                string jsonBody = JsonConvert.SerializeObject(requestBody);
                var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // Отправка запроса
                var response = await httpClient.PostAsync("chat/completions", httpContent);

                while (response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.BadGateway)
                {
                    await Task.Delay(2500); // Переводим секунды в миллисекунды
                    response = await httpClient.PostAsync("chat/completions", httpContent);
                }

                // Проверка успешности запроса
                response.EnsureSuccessStatusCode();

                // Чтение и вывод ответа
                var responseContent = await response.Content.ReadAsStringAsync();

                // Вывод сообщения
                var jsonResponse = JsonConvert.DeserializeObject<FreeGPTResponseFormatNEW>(responseContent);
                return jsonResponse.choices[0].Message.Content;
            };
        }
    }
    class FreeGPTAnswerFormat
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public object Refusal { get; set; }
    }

    class FreeGPTResponseFormatNEW
    {
        public string id { get; set; }
        public string model { get; set; }
        public string Object { get; set; }
        public int? created { get; set; }
        public List<FreeGPTAnswerFormatNEW> choices { get; set; }
        public string system_fingerprint { get; set; }
        public UsageAnswer usage { get; set; }
    }

    class FreeGPTAnswerFormatNEW
    {
        public int? Index { get; set; }
        public string finish_reason { get; set; }
        public FreeGPTAnswerFormat Message { get; set; }
    }

    class UsageAnswer
    {
        public int? PromtTokens { get; set; }
        public int? Complection_tokens { get; set; }
        public int? Total_tokens { get; set; }
        public double? cache_discount_rub { get; set; }
        public double? cost_rub { get; set; }
        public double? balance { get; set; }
    }
}
