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

    public class TradingSignal
    {
        [JsonProperty("action")]
        public string Action { get; set; }

        [JsonProperty("entry_price")]
        public decimal EntryPrice { get; set; }

        [JsonProperty("stop_loss")]
        public decimal StopLoss { get; set; }

        [JsonProperty("take_profit")]
        public decimal TakeProfit { get; set; }

        [JsonProperty("confidence")]
        public decimal Confidence { get; set; }

        [JsonProperty("success_probability")]
        public decimal SuccessProbability { get; set; }

        [JsonProperty("long_probability")]
        public decimal LongProbability { get; set; }

        [JsonProperty("short_probability")]
        public decimal ShortProbability { get; set; }

        [JsonProperty("investment_amount_usd")]
        public decimal InvestmentAmountUsd { get; set; }

        [JsonProperty("investment_percent")]
        public decimal InvestmentPercent { get; set; }

        [JsonProperty("hold_time")]
        public int HoldTime { get; set; }
    }
    public class BybitPosition
    {
        public string Symbol { get; set; } = default!;
        public string Side { get; set; } = default!; // "Buy" = лонг, "Sell" = шорт
        public decimal Size { get; set; } // количество базовой валюты (например, BTC)
        public decimal EntryPrice { get; set; }
        public decimal MarkPrice { get; set; }
        public decimal PositionValue { get; set; } // номинал в USDT
        public int Leverage { get; set; }
        public decimal? TakeProfit { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal UnrealizedPnl { get; set; }
        public decimal LiqPrice { get; set; }
    }
    public class BybitOrder
    {
        public string OrderId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty; // "Buy" или "Sell"
        public decimal Qty { get; set; } // количество контрактов/активов
        public decimal? Price { get; set; } // null для рыночных ордеров
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public string Status { get; set; } = string.Empty; // "New", "Filled", "Cancelled", "PartiallyFilled", "Untriggered" и т.д.
        public string OrderType { get; set; } = string.Empty; // "Limit", "Market", "Stop", "StopLimit" и т.п.
        public string? ReduceOnly { get; set; } // "true"/"false" или bool, в зависимости от API
        public string? TimeInForce { get; set; } // "GTC", "IOC", "FOK"
        public long CreatedTime { get; set; } // Unix timestamp в миллисекундах
        public long? UpdatedTime { get; set; } // Unix timestamp в миллисекундах
    }
}