using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Text.Json;

namespace CRYPTOAnalize.Services
{
    /// <summary>
    /// Bybit USDT-фьючерсный клиент с автоматической торговлей по цене входа.
    /// </summary>
    public class BybitService
    {
        // 🔁 Переключение: Testnet / Mainnet
        private const string BaseUrl = "https://api-testnet.bybit.com"; // ← замените на "https://api.bybit.com" для реальной торговли

        private readonly string _apiKey;
        private readonly string _secretKey;
        private readonly HttpClient _httpClient;

        public BybitService(string apiKey, string secretKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Полностью автоматический метод: создаёт conditional ордер на лонг или шорт
        /// на основе желаемой цены входа и размера позиции в USDT.
        /// </summary>
        /// <param name="symbol">Символ (например, "BTCUSDT")</param>
        /// <param name="entryPrice">Цена, при которой вы хотите войти (в USDT)</param>
        /// <param name="positionSizeUsdt">Размер позиции в USDT (номинал, не маржа)</param>
        /// <param name="takeProfit">Цена take-profit (в USDT)</param>
        /// <param name="stopLoss">Цена stop-loss (в USDT)</param>
        /// <param name="leverage">Плечо (по умолчанию 10)</param>
        public async Task<JsonDocument> PlaceConditionalTradeAsync(
            string symbol,
            decimal entryPrice,
            decimal positionSizeUsdt,
            decimal? takeProfit = null,
            decimal? stopLoss = null,
            int leverage = 10)
        {
            if (positionSizeUsdt <= 0)
                throw new ArgumentException("Position size must be > 0", nameof(positionSizeUsdt));

            // 1. Получаем текущую рыночную цену
            decimal currentPrice = await GetLastPriceAsync(symbol);

            // 2. Определяем направление
            string side;
            if (entryPrice > currentPrice)
            {
                side = "Buy"; // ожидаем рост → лонг
            }
            else if (entryPrice < currentPrice)
            {
                side = "Sell"; // ожидаем падение → шорт
            }
            else
            {
                throw new ArgumentException("Entry price must be different from current market price.", nameof(entryPrice));
            }

            // 3. Проверяем логичность TP/SL
            ValidateTpSl(entryPrice, takeProfit, stopLoss, side);

            // 4. Устанавливаем плечо (можно закомментировать, если уже установлено)
            await SetLeverageAsync(symbol, leverage);

            // 5. Отправляем conditional ордер
            var endpoint = "/private/linear/stop-order/create";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Рассчитываем qty = номинал / цена входа
            decimal qty = Math.Floor(positionSizeUsdt / entryPrice * 100_000_000m) / 100_000_000m;
            if (qty <= 0)
                throw new InvalidOperationException("Calculated quantity is zero. Position size too small.");

            var parameters = new Dictionary<string, string>
        {
            { "api_key", _apiKey },
            { "symbol", symbol },
            { "side", side },
            { "order_type", "Market" },
            { "qty", qty.ToString(CultureInfo.InvariantCulture) },
            { "stop_px", entryPrice.ToString(CultureInfo.InvariantCulture) },
            { "base_price", currentPrice.ToString(CultureInfo.InvariantCulture) },
            { "trigger_by", "LastPrice" },
            { "time_in_force", "GoodTillCancel" },
            { "position_idx", "0" },
            { "close_on_trigger", "false" },
            { "timestamp", timestamp.ToString() }
        };

            if (takeProfit.HasValue)
                parameters["take_profit"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);

            if (stopLoss.HasValue)
                parameters["stop_loss"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);

            if (takeProfit.HasValue)
                parameters["tp_trigger_by"] = "LastPrice";

            if (stopLoss.HasValue)
                parameters["sl_trigger_by"] = "LastPrice";

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);
            return JsonDocument.Parse(response);
        }

        // =============== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===============

        private async Task<decimal> GetLastPriceAsync(string symbol)
        {
            var url = $"{BaseUrl}/public/linear/tickers?symbol={symbol}";
            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("ret_code").GetInt32() != 0)
                throw new Exception($"Failed to fetch price: {root.GetProperty("ret_msg").GetString()}");

            var result = root.GetProperty("result")[0];
            var lastPrice = result.GetProperty("last_price").GetString();
            return decimal.Parse(lastPrice, CultureInfo.InvariantCulture);
        }

        private async Task<JsonDocument> SetLeverageAsync(string symbol, int leverage)
        {
            var endpoint = "/private/linear/position/set-leverage";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
        {
            { "api_key", _apiKey },
            { "symbol", symbol },
            { "buy_leverage", leverage.ToString() },
            { "sell_leverage", leverage.ToString() },
            { "timestamp", timestamp }
        };

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);
            return JsonDocument.Parse(response);
        }

        private void ValidateTpSl(decimal entryPrice, decimal? tp, decimal? sl, string side)
        {
            if (tp.HasValue && sl.HasValue && tp.Value == sl.Value)
                throw new ArgumentException("Take profit and stop loss cannot be equal.");

            if (side == "Buy")
            {
                if (tp.HasValue && tp.Value <= entryPrice)
                    throw new ArgumentException("For long, take profit must be > entry price.");
                if (sl.HasValue && sl.Value >= entryPrice)
                    throw new ArgumentException("For long, stop loss must be < entry price.");
            }
            else if (side == "Sell")
            {
                if (tp.HasValue && tp.Value >= entryPrice)
                    throw new ArgumentException("For short, take profit must be < entry price.");
                if (sl.HasValue && sl.Value <= entryPrice)
                    throw new ArgumentException("For short, stop loss must be > entry price.");
            }
        }

        private string BuildSignedContent(Dictionary<string, string> parameters)
        {
            var sortedParams = new List<string>();
            foreach (var kvp in parameters)
                sortedParams.Add($"{kvp.Key}={kvp.Value}");
            sortedParams.Sort();
            var queryString = string.Join("&", sortedParams);
            var signature = ComputeSignature(queryString, _secretKey);
            return queryString + $"&sign={signature}";
        }

        private async Task<string> SendSignedRequestAsync(string endpoint, string content)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            var response = await _httpClient.SendAsync(httpRequest);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Bybit API Error ({response.StatusCode}): {responseString}");

            return responseString;
        }

        private static string ComputeSignature(string payload, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
