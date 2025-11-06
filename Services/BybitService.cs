using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Text.Json;
using CryptoAnalyzer.Models;

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
        public BybitService()
        {

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
            int leverage = 10,
            string side = "Buy")
        {
            if (positionSizeUsdt <= 0)
                throw new ArgumentException("Position size must be > 0", nameof(positionSizeUsdt));

            // 1. Получаем текущую рыночную цену
            decimal currentPrice = await GetLastPriceAsync(symbol);

            // 2. Определяем направление
            if (entryPrice < currentPrice)
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

        /// <summary>
        /// Получает баланс USDT на фьючерсном счёте Bybit (linear).
        /// </summary>
        /// <returns>Баланс в USDT</returns>
        public async Task<decimal> GetUsdtBalanceAsync()
        {
            // Эндпоинт для получения кошелька (wallet balance)
            var endpoint = "/v2/private/wallet/balance";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "coin", "USDT" },        // ← можно заменить на другую валюту при необходимости
                { "timestamp", timestamp }
            };

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("ret_code").GetInt32() != 0)
                throw new Exception($"Failed to fetch balance: {root.GetProperty("ret_msg").GetString()}");

            // Путь к балансу: result.USDT.available_balance
            var usdtData = root.GetProperty("result").GetProperty("USDT");
            var balanceStr = usdtData.GetProperty("available_balance").GetString();
            return decimal.Parse(balanceStr, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Обновляет Take Profit и/или Stop Loss у открытой позиции.
        /// </summary>
        public async Task<JsonDocument> UpdatePositionTpSlAsync(
            string symbol,
            decimal? takeProfit = null,
            decimal? stopLoss = null,
            string? tpTriggerBy = "LastPrice",
            string? slTriggerBy = "LastPrice")
        {
            var endpoint = "/private/linear/position/trading-stop";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "symbol", symbol },
                { "timestamp", timestamp }
            };

            if (takeProfit.HasValue)
                parameters["take_profit"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);

            if (stopLoss.HasValue)
                parameters["stop_loss"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);

            if (takeProfit.HasValue)
                parameters["tp_trigger_by"] = tpTriggerBy;

            if (stopLoss.HasValue)
                parameters["sl_trigger_by"] = slTriggerBy;

            // Для One-way mode position_idx = 0 (по умолчанию)
            parameters["position_idx"] = "0";

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);
            return JsonDocument.Parse(response);
        }

        /// <summary>
        /// Добавляет к существующей позиции (усреднение).
        /// </summary>
        public async Task<JsonDocument> AddToPositionAsync(
            string symbol,
            string side, // "Buy" для лонга, "Sell" для шорта
            decimal additionalSizeUsdt) // дополнительный номинал в USDT
        {
            // Получаем текущую цену для расчёта qty
            var currentPrice = await GetLastPriceAsync(symbol);
            decimal qty = Math.Floor(additionalSizeUsdt / currentPrice * 100_000_000m) / 100_000_000m;
            if (qty <= 0) throw new ArgumentException("Слишком маленький размер для добавления.");

            var endpoint = "/private/linear/order/create";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "symbol", symbol },
                { "side", side },
                { "order_type", "Market" },
                { "qty", qty.ToString(CultureInfo.InvariantCulture) },
                { "time_in_force", "GoodTillCancel" },
                { "position_idx", "0" },
                { "timestamp", timestamp }
                // ❗ НЕ устанавливаем reduce_only — мы НЕ закрываем, а увеличиваем
            };

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);
            return JsonDocument.Parse(response);
        }

        /// <summary>
        /// Частично закрывает позицию (уменьшает размер).
        /// </summary>
        public async Task<JsonDocument> ReducePositionAsync(
            string symbol,
            string closeSide, // Противоположная сторона: если позиция Buy → closeSide = "Sell"
            decimal reduceSizeUsdt) // сколько USDT номинала закрыть
        {
            var currentPrice = await GetLastPriceAsync(symbol);
            decimal qty = Math.Floor(reduceSizeUsdt / currentPrice * 100_000_000m) / 100_000_000m;
            if (qty <= 0) throw new ArgumentException("Слишком маленький размер для закрытия.");

            var endpoint = "/private/linear/order/create";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "symbol", symbol },
                { "side", closeSide },
                { "order_type", "Market" },
                { "qty", qty.ToString(CultureInfo.InvariantCulture) },
                { "time_in_force", "GoodTillCancel" },
                { "reduce_only", "true" }, // ← КЛЮЧЕВОЙ ПАРАМЕТР: только уменьшение
                { "position_idx", "0" },
                { "timestamp", timestamp }
            };

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);
            return JsonDocument.Parse(response);
        }

        /// <summary>
        /// Получает информацию об открытой позиции по символу.
        /// Возвращает null, если позиция закрыта.
        /// </summary>
        public async Task<BybitPosition?> GetOpenPositionAsync(string symbol)
        {
            var endpoint = "/private/linear/position/list";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "symbol", symbol },
                { "timestamp", timestamp }
            };

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("ret_code").GetInt32() != 0)
                throw new Exception($"Failed to fetch position: {root.GetProperty("ret_msg").GetString()}");

            var result = root.GetProperty("result");
            if (result.ValueKind == JsonValueKind.Array && result.GetArrayLength() == 0)
                return null; // позиция не открыта

            // В One-way mode всегда одна запись
            var positionElement = result.ValueKind == JsonValueKind.Object
                ? result
                : result[0];

            // Проверяем, открыта ли позиция
            var size = decimal.Parse(positionElement.GetProperty("size").GetString(), CultureInfo.InvariantCulture);
            if (size == 0)
                return null;

            return new BybitPosition
            {
                Symbol = positionElement.GetProperty("symbol").GetString(),
                Side = positionElement.GetProperty("side").GetString(), // "Buy" или "Sell"
                Size = size,
                EntryPrice = decimal.Parse(positionElement.GetProperty("entry_price").GetString(), CultureInfo.InvariantCulture),
                MarkPrice = decimal.Parse(positionElement.GetProperty("mark_price").GetString(), CultureInfo.InvariantCulture),
                PositionValue = decimal.Parse(positionElement.GetProperty("position_value").GetString(), CultureInfo.InvariantCulture),
                Leverage = int.Parse(positionElement.GetProperty("leverage").GetString()),
                TakeProfit = TryParseDecimal(positionElement, "take_profit"),
                StopLoss = TryParseDecimal(positionElement, "stop_loss"),
                UnrealizedPnl = decimal.Parse(positionElement.GetProperty("unrealised_pnl").GetString(), CultureInfo.InvariantCulture),
                LiqPrice = decimal.Parse(positionElement.GetProperty("liq_price").GetString(), CultureInfo.InvariantCulture)
            };
        }

        public async Task<BybitOrder?> GetActiveOrderAsync(string symbol, string side = null)
        {
            var endpoint = "/private/linear/order/list";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
            {
                { "api_key", _apiKey },
                { "symbol", symbol },
                { "timestamp", timestamp }
            };

            if (!string.IsNullOrEmpty(side))
                parameters["side"] = side;

            var content = BuildSignedContent(parameters);
            var response = await SendSignedRequestAsync(endpoint, content);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("ret_code").GetInt32() != 0)
                throw new Exception($"Failed to fetch orders: {root.GetProperty("ret_msg").GetString()}");

            // Важно: в v3+ используется result.data
            var data = root.GetProperty("result").GetProperty("data");

            foreach (var order in data.EnumerateArray())
            {
                var status = order.GetProperty("order_status").GetString();
                if (status == "New" || status == "Untriggered" || status == "PartiallyFilled")
                {
                    return new BybitOrder
                    {
                        OrderId = order.GetProperty("order_id").GetString(),
                        Symbol = order.GetProperty("symbol").GetString(),
                        Side = order.GetProperty("side").GetString(),
                        Qty = decimal.Parse(order.GetProperty("qty").GetString(), CultureInfo.InvariantCulture),
                        Price = TryParseDecimal(order, "price"),
                        StopLoss = TryParseDecimal(order, "stop_loss"),
                        TakeProfit = TryParseDecimal(order, "take_profit"),
                        Status = status
                    };
                }
            }

            return null;
        }

        // Вспомогательный метод для безопасного парсинга TP/SL (могут быть "0")
        private static decimal? TryParseDecimal(JsonElement element, string propertyName)
        {
            var value = element.GetProperty(propertyName).GetString();
            return value != "0" && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
                ? result
                : null;
        }

        private static readonly HttpClient client = new HttpClient();
        public async Task<TopMovers> GetTop10GainersAndLosersWith25WeeksAsync()
        {
            try
            {
                // Шаг 1: Получить все 24h тикеры из категории linear (USDT perpetuals)
                var tickersUrl = "https://api.bybit.com/v5/market/tickers?category=linear";
                var tickersResponseJson = await client.GetStringAsync(tickersUrl);
                var tickersResponse = JsonConvert.DeserializeObject<BybitTickersResponse>(tickersResponseJson);

                if (tickersResponse?.Result?.List == null)
                    throw new InvalidOperationException("Invalid tickers response from Bybit");

                var allTickers = tickersResponse.Result.List;

                // Фильтруем пары, заканчивающиеся на USDT (в Bybit они так и называются, например BTCUSDT)
                var usdtTickers = allTickers
                    .Where(t => t.Symbol.EndsWith("USDT") && t.Symbol.Length > 5)
                    .Select(t =>
                    {
                        var raw = t.Price24hPcnt;
                        var success = decimal.TryParse(
                            raw,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var pct
                        );

                        return new
                        {
                            Symbol = t.Symbol,
                            ChangePercent = success ? pct * 100m : 0m // Bybit возвращает доли (0.0251 = 2.51%), умножаем на 100
                        };
                    })
                    .ToList();

                // Сортируем по изменению
                var sorted = usdtTickers.OrderByDescending(x => x.ChangePercent).ToList();

                // Берём расширенный пул кандидатов
                var candidateSymbols = sorted
                    .Take(30)
                    .Concat(sorted.Skip(Math.Max(0, sorted.Count - 30)))
                    .Select(x => x.Symbol)
                    .Distinct()
                    .ToList();

                // Шаг 2: Фильтруем только те, у которых >=25 недельных свечей
                var validSymbols = new List<(string Symbol, decimal ChangePercent)>();
                foreach (var symbol in candidateSymbols)
                {
                    var weeksCount = await GetWeeklyCandleCountAsync(symbol);
                    if (weeksCount >= 25)
                    {
                        var pct = usdtTickers.First(x => x.Symbol == symbol).ChangePercent;
                        validSymbols.Add((symbol, pct));
                    }
                }

                // Сортируем валидные по изменению
                var validSorted = validSymbols.OrderByDescending(x => x.ChangePercent).ToList();

                var gainers = validSorted
                    .Take(10)
                    .Select(x => new Ticker24hr
                    {
                        Symbol = x.Symbol,
                        PriceChangePercent = x.ChangePercent.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList();

                var losers = validSorted
                    .OrderBy(x => x.ChangePercent)
                    .Take(10)
                    .Select(x => new Ticker24hr
                    {
                        Symbol = x.Symbol,
                        PriceChangePercent = x.ChangePercent.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList();

                return new TopMovers
                {
                    TopGainers = gainers,
                    TopLosers = losers
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to fetch top movers with 25+ weeks history from Bybit", ex);
            }
        }

        private async Task<int> GetWeeklyCandleCountAsync(string symbol)
        {
            var url = $"https://api.bybit.com/v5/market/kline?category=linear&symbol={symbol}&interval=W&limit=200";
            try
            {
                var json = await client.GetStringAsync(url);
                var response = JsonConvert.DeserializeObject<BybitKlineResponse>(json);
                return response?.Result?.List?.Count ?? 0;
            }
            catch (HttpRequestException)
            {
                return 0;
            }
        }

        // Вспомогательные DTO для Bybit v5 API
        private class BybitTickersResponse
        {
            public BybitTickersResult Result { get; set; }
        }

        private class BybitTickersResult
        {
            public List<BybitTicker> List { get; set; }
        }

        private class BybitTicker
        {
            public string Symbol { get; set; }
            public string Price24hPcnt { get; set; } // строка вида "0.0251"
        }

        private class BybitKlineResponse
        {
            public BybitKlineResult Result { get; set; }
        }

        private class BybitKlineResult
        {
            public string Symbol { get; set; } // опционально
            public List<List<string>> List { get; set; }
        }
    }
}
