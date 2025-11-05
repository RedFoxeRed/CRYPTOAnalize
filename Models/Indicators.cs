using Newtonsoft.Json;

namespace CryptoAnalyzer.Models
{
    public class Indicators
    {
        [JsonProperty("rsi_14")]
        public double? Rsi14 { get; set; }

        [JsonProperty("macd")]
        public MacdData? Macd { get; set; }

        [JsonProperty("boll")]
        public BollData? Boll { get; set; }  // ✅ Исправлено: правильное имя и тип
    }

    public class MacdData
    {
        [JsonProperty("macd_line")]
        public double? MacdLine { get; set; }

        [JsonProperty("signal_line")]
        public double? SignalLine { get; set; }

        [JsonProperty("histogram")]
        public double? Histogram { get; set; }
    }

    // ✅ Новый класс для Bollinger Bands
    public class BollData
    {
        [JsonProperty("upper")]
        public double? Upper { get; set; }

        [JsonProperty("middle")]
        public double? Middle { get; set; }

        [JsonProperty("lower")]
        public double? Lower { get; set; }
    }
}