using CryptoAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CRYPTOAnalize.Services
{
    public class PromtSender
    {
        public static async Task<TradingSignal> GetSignalDataFromLLM(Ticker24hr ticker, string json, decimal balance)
        {
            return new TradingSignal();
        }

        public static async Task<TradingSignal> UpdateSignalFromLLM(Ticker24hr ticker, string newJsonData, TradingSignal LLM_signal, decimal balance)
        {
            return new TradingSignal();
        }
    }
}
