using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CryptoAnalyzer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Skender.Stock.Indicators;

namespace CryptoAnalyzer.Services
{
    public class BinanceService
    {
        private static readonly HttpClient client = new HttpClient();

        public async Task<MarketSnapshot> GetMarketSnapshotAsync(string symbol, string[] intervals)
        {
            var snapshot = new MarketSnapshot
            {
                Symbol = symbol,
                Timestamp = DateTimeOffset.UtcNow,
                Timeframes = new Dictionary<string, TimeframeData>()
            };

            foreach (var interval in intervals)
            {
                // Запрашиваем 100 свечей для индикаторов
                var candles = await FetchCandlesAsync(symbol, interval, 100);
                if (candles.Count == 0) continue;

                // === Подготовка данных для индикаторов (на всех 100 свечах) ===
                var quotes = candles.Select(c => new Skender.Stock.Indicators.Quote
                {
                    Date = c.OpenTime.UtcDateTime,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume
                }).ToList();

                // === RSI ===
                double? rsi = null;
                var rsiResults = quotes.GetRsi(14).ToList();
                if (rsiResults.Count > 0)
                {
                    rsi = rsiResults[rsiResults.Count - 1].Rsi;
                }

                // === MACD ===
                MacdData macd = null;
                var macdResults = quotes.GetMacd().ToList();
                if (macdResults.Count > 0)
                {
                    var lastMacd = macdResults[macdResults.Count - 1];
                    macd = new MacdData
                    {
                        MacdLine = lastMacd.Macd,
                        SignalLine = lastMacd.Signal,
                        Histogram = lastMacd.Histogram
                    };
                }

                // === Bollinger Bands ===
                var bollResults = quotes.GetBollingerBands(lookbackPeriods: 20, standardDeviations: 2).ToList();

                BollingerBandsResult? boll = null;
                if (bollResults.Count > 0)
                {
                    boll = bollResults[^1]; // или bollResults[bollResults.Count - 1]
                }

                // === Bollinger Bands ===
                double? bollUpper = null, bollMiddle = null, bollLower = null;
                if (bollResults.Count > 0)
                {
                    var lastBoll = bollResults[^1];
                    bollUpper = lastBoll.UpperBand;
                    bollMiddle = lastBoll.Sma;
                    bollLower = lastBoll.LowerBand;
                }

                // === Берём последние 20 свечей для сохранения в JSON ===
                var last20Candles = candles.Skip(Math.Max(0, candles.Count - 25)).Take(25).ToList();

                var compactCandles = new CandlesData();
                foreach (var c in last20Candles)
                {
                    compactCandles.OpenTimeReadable.Add(c.OpenTime.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
                    compactCandles.Open.Add(c.Open);
                    compactCandles.High.Add(c.High);
                    compactCandles.Low.Add(c.Low);
                    compactCandles.Close.Add(c.Close);
                    compactCandles.Volume.Add(c.Volume);
                    compactCandles.BuyVolume.Add(c.BuyVolume);
                }

                // === Сохраняем в снапшот ===
                snapshot.Timeframes[interval] = new TimeframeData
                {
                    Interval = interval,
                    Candles = compactCandles,
                    Indicators = new Indicators
                    {
                        Rsi14 = rsi,
                        Macd = macd,
                        Boll = bollUpper.HasValue ? new BollData
                        {
                            Upper = bollUpper,
                            Middle = bollMiddle,
                            Lower = bollLower
                        } : null
                    }
                };
            }

            return snapshot;
        }

        private async Task<List<Candle>> FetchCandlesAsync(string symbol, string interval, int limit)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";
            var json = await client.GetStringAsync(url);
            var data = JArray.Parse(json);

            var candles = new List<Candle>();
            foreach (var row in data)
            {
                candles.Add(new Candle
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds((long)row[0]),
                    Open = (decimal)row[1],
                    High = (decimal)row[2],
                    Low = (decimal)row[3],
                    Close = (decimal)row[4],
                    Volume = (decimal)row[5],
                    BuyVolume = (decimal)row[9] // taker buy base asset volume
                });
            }

            return candles;
        }

        public async Task<TopMovers> GetTop10GainersAndLosersWith25WeeksAsync()
        {
            try
            {
                Thread.Sleep(1000);
                // Шаг 1: Получить все 24h тикеры
                var allTickersJson = await client.GetStringAsync("https://api.binance.com/api/v3/ticker/24hr");
                var allTickers = JsonConvert.DeserializeObject<List<Ticker24hr>>(allTickersJson);

                // Фильтруем USDT-пары (и избегаем артефактов вроде BUSDT)
                var usdtTickers = allTickers
                .Where(t => t.Symbol.EndsWith("USDT") && t.Symbol.Length > 5)
                .Select(t =>
                {
                    var raw = t.PriceChangePercent;
                    var success = decimal.TryParse(
                        raw,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var pct
                    );

                    // Опционально: лог для отладки
                    // Console.WriteLine($"{t.Symbol}: raw='{raw}', parsed={pct}, success={success}");

                    return new
                    {
                        Symbol = t.Symbol,
                        ChangePercent = success ? pct : 0m
                    };
                })
                .ToList();

                // Сортируем по изменению
                var sorted = usdtTickers.OrderByDescending(x => x.ChangePercent).ToList();

                // Берём расширенный кандидатский пул (на случай, что часть не пройдёт фильтр по свечам)
                var candidateSymbols = sorted
                    .Take(30) // топ-30 гейнеров
                    .Concat(sorted.Skip(Math.Max(0, sorted.Count - 30))) // топ-30 лузеров
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
                        PriceChangePercent = x.ChangePercent.ToString() // или ToString()
                    })
                    .ToList();

                var losers = validSorted
                    .OrderBy(x => x.ChangePercent) // сортируем по возрастанию (самые низкие — самые падающие)
                    .Take(10)
                    .Select(x => new Ticker24hr
                    {
                        Symbol = x.Symbol,
                        PriceChangePercent = x.ChangePercent.ToString()
                    })
                    .ToList();

                // Если не хватает — обрежем/дополним пустыми (по вашему усмотрению)
                return new TopMovers
                {
                    TopGainers = gainers,
                    TopLosers = losers
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to fetch top movers with 25+ weeks history", ex);
            }
        }

        private async Task<int> GetWeeklyCandleCountAsync(string symbol)
        {
            // Запрашиваем до 1000 недельных свечей (максимум, что даёт Binance за раз)
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1w&limit=1000";
            try
            {
                var json = await client.GetStringAsync(url);
                var candles = JsonConvert.DeserializeObject<List<List<object>>>(json);
                return candles?.Count ?? 0;
            }
            catch (HttpRequestException)
            {
                // Если пара не поддерживает недельные свечи (редко, но бывает), считаем 0
                return 0;
            }
        }
    }
}