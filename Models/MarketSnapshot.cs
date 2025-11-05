using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CryptoAnalyzer.Models
{
    public class MarketSnapshot
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonProperty("timeframes")]
        public Dictionary<string, TimeframeData> Timeframes { get; set; } = new();
    }

    public class TimeframeData
    {
        [JsonProperty("interval")]
        public string Interval { get; set; }

        // Будет содержать последние 20 свечей в компактном виде
        [JsonProperty("candles")]
        public CandlesData Candles { get; set; }

        [JsonProperty("indicators")]
        public Indicators Indicators { get; set; }
    }

    public class Candle
    {
        [JsonProperty("open_time")]
        public DateTimeOffset OpenTime { get; set; }

        [JsonProperty("open")]
        public decimal Open { get; set; }

        [JsonProperty("high")]
        public decimal High { get; set; }

        [JsonProperty("low")]
        public decimal Low { get; set; }

        [JsonProperty("close")]
        public decimal Close { get; set; }

        [JsonProperty("volume")]
        public decimal Volume { get; set; }

        [JsonProperty("buy_volume")]
        public decimal BuyVolume { get; set; }

        [JsonProperty("sell_volume")]
        public decimal SellVolume => Volume - BuyVolume;

        [JsonProperty("buy_volume_ratio")]
        public double BuyVolumeRatio => Volume == 0 ? 0 : (double)(BuyVolume / Volume);
    }

    public class CandlesData
    {
        [JsonProperty("open_time")]
        public List<string> OpenTimeReadable { get; set; } = new();

        [JsonProperty("open")]
        public List<decimal> Open { get; set; } = new();

        [JsonProperty("high")]
        public List<decimal> High { get; set; } = new();

        [JsonProperty("low")]
        public List<decimal> Low { get; set; } = new();

        [JsonProperty("close")]
        public List<decimal> Close { get; set; } = new();

        [JsonProperty("volume")]
        public List<decimal> Volume { get; set; } = new();

        [JsonProperty("buy_volume")]
        public List<decimal> BuyVolume { get; set; } = new();
    }

    public class Ticker24hr
    {
        public string Symbol { get; set; }
        public string PriceChangePercent { get; set; }
    }

    public class TopMovers
    {
        public List<Ticker24hr> TopGainers { get; set; } = new();
        public List<Ticker24hr> TopLosers { get; set; } = new();
    }
}