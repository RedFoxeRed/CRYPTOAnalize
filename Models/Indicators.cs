using Newtonsoft.Json;

namespace CryptoAnalyzer.Models
{
    public class Indicators
    {
        [JsonProperty("rsi_14")]
        public double? Rsi14 { get; set; }

        [JsonProperty("macd")]
        public MacdData Macd { get; set; }
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
}