using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CryptoAnalyzer.Models;
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

                // === Берём последние 20 свечей для сохранения в JSON ===
                var last20Candles = candles.Skip(Math.Max(0, candles.Count - 25)).Take(25).ToList();

                var compactCandles = new CandlesData();
                foreach (var c in last20Candles)
                {
                    compactCandles.OpenTimeUnixMs.Add(c.OpenTime.ToUnixTimeMilliseconds());
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
                        Macd = macd
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
    }
}