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
using Newtonsoft.Json.Linq;

namespace CRYPTOAnalize.Services
{
    /// <summary>
    /// Bybit USDT-фьючерсный клиент с автоматической торговлей по цене входа.
    /// </summary>
    public class BybitService
    {
        // 🔁 Переключение: Testnet / Mainnet
        private const string BaseUrl = "https://api.bybit.com"; // ← замените на "https://api.bybit.com" для реальной торговли

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

            // В unified-аккаунте плечо не указывается на символ — оно глобальное
            // (можно пропустить SetLeverage, или задать один раз в UI)

            // Определяем side: Buy = лонг (вход выше рынка), Sell = шорт (вход ниже рынка)
            // Но в unified лонг/шорт управляется через режим позиции (Hedge/One-way)
            // Мы предполагаем One-way → "Buy" = лонг, "Sell" = шорт

            // Рассчитываем количество в базовой валюте (например, BTC для BTCUSDT)
            var (lotSize, minQty) = await GetSymbolInfoAsync(symbol);

            decimal rawQty = positionSizeUsdt / entryPrice;
            decimal qty = Math.Floor(rawQty / lotSize) * lotSize; // округление вниз до шага

            if (qty < minQty)
                throw new ArgumentException($"Calculated qty ({qty}) is below minimum ({minQty}) for {symbol}");

            // Убедитесь, что qty > 0
            if (qty <= 0)
                throw new InvalidOperationException("Qty is zero after rounding.");

            //var parameters = new Dictionary<string, string>
            //{
            //    { "category", "linear" }, // обязательный параметр в v5
            //    { "symbol", symbol.ToUpper() },
            //    { "side", side }, // "Buy" или "Sell"
            //    { "orderType", "Limit" },
            //    { "qty", qty.ToString(CultureInfo.InvariantCulture) },
            //    { "price", entryPrice.ToString(CultureInfo.InvariantCulture) },
            //    { "timeInForce", "GTC" },
            //    { "positionIdx", "0" }, // 0 = one-way mode
            //    { "api_key", _apiKey },
            //    { "recvWindow", "60000" },
            //    { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
            //};

            //if (takeProfit.HasValue)
            //    parameters["takeProfit"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);

            //if (stopLoss.HasValue)
            //    parameters["stopLoss"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);

            //if (takeProfit.HasValue || stopLoss.HasValue)
            //    parameters["tpslMode"] = "Full"; // или "Partial"
            var parameters = new Dictionary<string, string>
            {
                { "category", "linear" },
                { "symbol", symbol.ToUpper() },
                { "side", side },
                { "orderType", "Limit" },
                { "qty", qty.ToString(CultureInfo.InvariantCulture) },
                { "price", entryPrice.ToString(CultureInfo.InvariantCulture) },
                { "timeInForce", "GTC" },
                { "positionIdx", "0" }
            };

            if (takeProfit.HasValue)
                parameters["takeProfit"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);

            if (stopLoss.HasValue)
                parameters["stopLoss"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);

            if (takeProfit.HasValue || stopLoss.HasValue)
                parameters["tpslMode"] = "Full";
            //parameters["qty"] = qty.ToString(CultureInfo.InvariantCulture);

            var response = await SendSignedV5RequestAsync("/v5/order/create", parameters, HttpMethod.Post);
            return JsonDocument.Parse(response);
        }

        // =============== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ===============
        public async Task<(decimal LotSize, decimal MinOrderQty)> GetSymbolInfoAsync(string symbol)
        {
            var url = $"{BaseUrl}/v5/market/instruments-info?category=linear&symbol={symbol.ToUpper()}";
            using var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement.GetProperty("result").GetProperty("list");
            var item = list[0];

            var lotSize = decimal.Parse(item.GetProperty("lotSizeFilter").GetProperty("qtyStep").GetString(), CultureInfo.InvariantCulture);
            var minQty = decimal.Parse(item.GetProperty("lotSizeFilter").GetProperty("minOrderQty").GetString(), CultureInfo.InvariantCulture);

            return (lotSize, minQty);
        }
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

        //private async Task SetLeverageAsync(string symbol, int leverage)
        //{
        //    var parameters = new Dictionary<string, string>
        //    {
        //        { "category", "linear" },
        //        { "symbol", symbol.ToUpper() },
        //        { "buyLeverage", leverage.ToString() },
        //        { "sellLeverage", leverage.ToString() }
        //    };

        //    await SendSignedV5RequestAsync("/v5/position/set-leverage", parameters, HttpMethod.Post);
        //}
        private async Task<string> SendSignedV5RequestAsync(string endpoint, Dictionary<string, string> parameters, HttpMethod method)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var recvWindow = "30000";

            // Обязательные параметры
            parameters["api_key"] = _apiKey;
            parameters["timestamp"] = timestamp;
            parameters["recvWindow"] = recvWindow;

            if (method == HttpMethod.Get)
            {
                // Для GET: параметры в query string (с URL-encoding)
                var sorted = parameters.OrderBy(p => p.Key)
                                       .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
                                       .ToArray();
                var queryString = string.Join("&", sorted);
                var signature = ComputeSignature(queryString, _secretKey);
                var fullUrl = $"{BaseUrl}{endpoint}?{queryString}&sign={signature}";

                using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                using var response = await _httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                //LogResponse(responseText);
                ValidateResponse(responseText);
                return responseText;
            }
            else if (method == HttpMethod.Post)
            {
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                recvWindow = "30000";

                // Убедитесь, что параметры не содержат лишнего (sign и т.п.)
                parameters["api_key"] = _apiKey;
                parameters["timestamp"] = timestamp;
                parameters["recvWindow"] = recvWindow;

                // Сериализуем тело — именно так, как уйдёт в запрос
                var jsonBody = JsonConvert.SerializeObject(parameters, Formatting.None);

                // 🔑 Формируем строку для подписи: timestamp + api_key + recvWindow + jsonBody
                string payloadToSign = timestamp + _apiKey + recvWindow + jsonBody;
                string signature = ComputeSignature(payloadToSign, _secretKey);

                // Отправляем запрос
                var fullUrl = $"{BaseUrl}{endpoint}";
                using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                request.Headers.Add("X-BAPI-SIGN", signature);
                request.Headers.Add("X-BAPI-API-KEY", _apiKey);
                request.Headers.Add("X-BAPI-TIMESTAMP", timestamp);
                request.Headers.Add("X-BAPI-RECV-WINDOW", recvWindow);

                using var response = await _httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                LogResponse(responseText);
                ValidateResponse(responseText);
                return responseText;
            }
            else
            {
                throw new NotSupportedException($"HTTP method {method} is not supported.");
            }
        }

        private void LogResponse(string responseText)
        {
            Console.WriteLine("Ответ Bybit: " + responseText);
        }

        private void ValidateResponse(string responseText)
        {
            try
            {
                var json = JObject.Parse(responseText);
                var retCode = json["retCode"]?.Value<int>() ?? -1;

                if (retCode != 0)
                {
                    var retMsg = json["retMsg"]?.ToString() ?? "Unknown error";
                    throw new Exception($"API Error ({retCode}): {retMsg}");
                }
            }
            catch (JsonReaderException ex)
            {
                throw new Exception($"Failed to parse JSON response: {ex.Message}. Raw: {responseText}");
            }
        }
        //private void ValidateTpSl(decimal entryPrice, decimal? tp, decimal? sl, string side)
        //{
        //    if (tp.HasValue && sl.HasValue && tp.Value == sl.Value)
        //        throw new ArgumentException("Take profit and stop loss cannot be equal.");

        //    if (side == "Buy")
        //    {
        //        if (tp.HasValue && tp.Value <= entryPrice)
        //            throw new ArgumentException("For long, take profit must be > entry price.");
        //        if (sl.HasValue && sl.Value >= entryPrice)
        //            throw new ArgumentException("For long, stop loss must be < entry price.");
        //    }
        //    else if (side == "Sell")
        //    {
        //        if (tp.HasValue && tp.Value >= entryPrice)
        //            throw new ArgumentException("For short, take profit must be < entry price.");
        //        if (sl.HasValue && sl.Value <= entryPrice)
        //            throw new ArgumentException("For short, stop loss must be > entry price.");
        //    }
        //}

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

        //private async Task<string> SendSignedRequestAsync(string endpoint, Dictionary<string, string> parameters)
        //{
        //    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        //    parameters["timestamp"] = timestamp;

        //    var sortedParams = parameters
        //        .OrderBy(kvp => kvp.Key)
        //        .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}")
        //        .ToList();

        //    var queryString = string.Join("&", sortedParams);
        //    var signature = ComputeSignature(queryString, _secretKey);
        //    var signedQueryString = queryString + $"&sign={signature}";

        //    // Убедитесь, что endpoint не содержит пробелов!
        //    var fullUrl = BaseUrl.TrimEnd('/') + "/" + endpoint.Trim('/') + "?" + signedQueryString;

        //    // Используем GET, а не POST!
        //    var httpRequest = new HttpRequestMessage(HttpMethod.Get, fullUrl);

        //    var response = await _httpClient.SendAsync(httpRequest);
        //    var responseString = await response.Content.ReadAsStringAsync();

        //    if (!response.IsSuccessStatusCode)
        //        throw new Exception($"Bybit API Error ({response.StatusCode}): {responseString}");

        //    return responseString;
        //}

        //private static string ComputeSignature(string payload, string secret)
        //{
        //    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        //    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        //    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        //}

        private static string ComputeSignature(string payload, string secret)
        {
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            using var hmac = new HMACSHA256(secretBytes);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var hashBytes = hmac.ComputeHash(payloadBytes);
            return BytesToHex(hashBytes); // или аналог
        }
        private static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Получает баланс USDT на фьючерсном счёте Bybit (linear).
        /// </summary>
        /// <returns>Баланс в USDT</returns>
        public async Task<decimal> GetUsdtBalanceAsync()
        {
            var endpoint = "/v5/account/wallet-balance";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            var parameters = new Dictionary<string, string>
    {
        { "api_key", _apiKey },
        { "accountType", "UNIFIED" },
        { "coin", "USDT" },
        { "recvWindow", "30000" },
        { "timestamp", timestamp }
    };

            var sorted = parameters.OrderBy(p => p.Key)
                                   .Select(p => $"{p.Key}={p.Value}")
                                   .ToArray();
            var queryString = string.Join("&", sorted);
            var signature = ComputeSignature(queryString, _secretKey);
            var url = $"https://api.bybit.com{endpoint}?{queryString}&sign={signature}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("retCode").GetInt32() != 0)
                throw new Exception($"API Error: {root.GetProperty("retMsg").GetString()}");

            var walletList = root.GetProperty("result").GetProperty("list");
            if (walletList.GetArrayLength() == 0)
                throw new Exception("No wallet data.");

            var unifiedWallet = walletList[0];
            var coins = unifiedWallet.GetProperty("coin").EnumerateArray();

            foreach (var coin in coins)
            {
                if (coin.GetProperty("coin").GetString() == "USDT")
                {
                    var balanceStr = coin.GetProperty("walletBalance").GetString();
                    return decimal.Parse(balanceStr, CultureInfo.InvariantCulture);
                }
            }

            throw new Exception("USDT not found in wallet.");
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
            // Только бизнес-параметры (уходят в JSON-тело)
            var bodyParams = new Dictionary<string, string>
            {
                { "category", "linear" },
                { "symbol", symbol.ToUpper() },
                { "positionIdx", "0" }
            };

            if (takeProfit.HasValue)
                bodyParams["takeProfit"] = takeProfit.Value.ToString(CultureInfo.InvariantCulture);

            if (stopLoss.HasValue)
                bodyParams["stopLoss"] = stopLoss.Value.ToString(CultureInfo.InvariantCulture);

            if (takeProfit.HasValue && !string.IsNullOrEmpty(tpTriggerBy))
                bodyParams["tpTriggerBy"] = tpTriggerBy;

            if (stopLoss.HasValue && !string.IsNullOrEmpty(slTriggerBy))
                bodyParams["slTriggerBy"] = slTriggerBy;

            var response = await SendSignedV5RequestAsync("/v5/position/set-trading-stop", bodyParams, HttpMethod.Post);
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
            var parameters = new Dictionary<string, string>
    {
        { "category", "linear" },
        { "symbol", symbol.ToUpper() },
        { "api_key", _apiKey },
        { "recvWindow", "30000" },
        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
    };

            var response = await SendSignedV5RequestAsync("/v5/position/list", parameters, HttpMethod.Get);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("retCode").GetInt32() != 0)
                throw new Exception($"API Error: {root.GetProperty("retMsg").GetString()}");

            var list = root.GetProperty("result").GetProperty("list");

            // В unified-аккаунте при one-way mode будет одна запись (или ноль)
            foreach (var pos in list.EnumerateArray())
            {
                // В v5 поле "size" — это строка, "0" означает закрытую позицию
                var sizeStr = pos.GetProperty("size").GetString();
                if (string.IsNullOrEmpty(sizeStr) || sizeStr == "0")
                    continue;

                var size = decimal.Parse(sizeStr, CultureInfo.InvariantCulture);
                if (size == 0)
                    continue;

                return new BybitPosition
                {
                    Symbol = pos.GetProperty("symbol").GetString(),
                    Side = DetermineSideFromPosition(pos), // см. ниже
                    Size = size,
                    EntryPrice = ParseDecimal(pos, "avgPrice"),      // ← entry_price → avgPrice
                    MarkPrice = ParseDecimal(pos, "markPrice"),
                    PositionValue = ParseDecimal(pos, "positionValue"),
                    Leverage = int.Parse(pos.GetProperty("leverage").GetString()),
                    TakeProfit = TryParseDecimal(pos, "takeProfit"),
                    StopLoss = TryParseDecimal(pos, "stopLoss"),
                    UnrealizedPnl = ParseDecimal(pos, "unrealisedPnl"),
                    LiqPrice = ParseDecimal(pos, "liqPrice")
                };
            }

            return null;
        }
        private static decimal ParseDecimal(JsonElement element, string propertyName)
        {
            var str = element.GetProperty(propertyName).GetString();
            return string.IsNullOrEmpty(str)
                ? 0
                : decimal.Parse(str, CultureInfo.InvariantCulture);
        }
        private static string DetermineSideFromPosition(JsonElement pos)
        {
            return pos.GetProperty("side").GetString(); // будет "Buy" или "Sell"
        }
        public async Task<BybitOrder?> GetActiveOrderAsync(string symbol, string side = null)
        {
            var parameters = new Dictionary<string, string>
    {
        { "category", "linear" },
        { "symbol", symbol.ToUpper() },
        { "api_key", _apiKey },
        { "recvWindow", "30000" },
        { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() }
    };

            // В v5 фильтрация по side делается уже после получения данных
            var response = await SendSignedV5RequestAsync("/v5/order/realtime", parameters, HttpMethod.Get);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.GetProperty("retCode").GetInt32() != 0)
                throw new Exception($"API Error: {root.GetProperty("retMsg").GetString()}");

            var list = root.GetProperty("result").GetProperty("list");

            foreach (var order in list.EnumerateArray())
            {
                var status = order.GetProperty("orderStatus").GetString();
                var orderSide = order.GetProperty("side").GetString();

                // Фильтруем по статусу (активные ордера)
                if (status is "New" or "PartiallyFilled" or "Untriggered")
                {
                    // Фильтрация по стороне, если указана
                    if (!string.IsNullOrEmpty(side) && !string.Equals(orderSide, side, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return new BybitOrder
                    {
                        OrderId = order.GetProperty("orderId").GetString(),
                        Symbol = order.GetProperty("symbol").GetString(),
                        Side = orderSide,
                        Qty = decimal.Parse(order.GetProperty("qty").GetString(), CultureInfo.InvariantCulture),
                        Price = TryParseDecimal(order, "price"),
                        StopLoss = TryParseDecimal(order, "stopLoss"),
                        TakeProfit = TryParseDecimal(order, "takeProfit"),
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

                var allTickers = tickersResponse.Result.List.Where(x => Math.Abs(Convert.ToDouble(x.Price24hPcnt.Replace(".", ","))) > 0.035);

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
                //var validSymbols = new List<(string Symbol, decimal ChangePercent)>();
                //foreach (var symbol in candidateSymbols)
                //{
                //    var weeksCount = await GetWeeklyCandleCountAsync(symbol);
                //    if (weeksCount >= 25)
                //    {
                //        var pct = usdtTickers.First(x => x.Symbol == symbol).ChangePercent;
                //        validSymbols.Add((symbol, pct));
                //    }
                //}

                var semaphore = new SemaphoreSlim(10); // Ограничение: 6 параллельных запросов
                var tasks = new List<Task<(string Symbol, int Count)?>>();

                foreach (var symbol in candidateSymbols)
                {
                    tasks.Add(ProcessSymbolAsync(symbol, semaphore));
                }

                var results = await Task.WhenAll(tasks);
                var validSymbols = results
                    .Where(r => r.HasValue && r.Value.Count >= 25)
                    .Select(r =>
                    {
                        var ticker = usdtTickers.First(x => x.Symbol == r.Value.Symbol);
                        return (r.Value.Symbol, ticker.ChangePercent);
                    })
                    .ToList();

                async Task<(string Symbol, int Count)?> ProcessSymbolAsync(string symbol, SemaphoreSlim sem)
                {
                    await sem.WaitAsync();
                    try
                    {
                        var count = await GetWeeklyCandleCountAsync(symbol);
                        return (symbol, count);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }

                // Сортируем валидные по изменению
                var validSorted = validSymbols.OrderByDescending(x => x.ChangePercent).ToList();

                var gainers = validSorted
                    .Take(15)
                    .Select(x => new Ticker24hr
                    {
                        Symbol = x.Symbol,
                        PriceChangePercent = x.ChangePercent.ToString(CultureInfo.InvariantCulture)
                    })
                    .ToList();

                var losers = validSorted
                    .OrderBy(x => x.ChangePercent)
                    .Take(15)
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
