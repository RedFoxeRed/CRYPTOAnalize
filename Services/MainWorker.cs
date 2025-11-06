using CryptoAnalyzer.Models;
using CryptoAnalyzer.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CRYPTOAnalize.Services
{
    public class MainWorker
    {
        public async Task StartToWorkAsync(CancellationToken cancelToken)
        {
            var service = new BinanceService();
            var bybitService = new BybitService("ikgZnYicSZui5G2eWe", "ikgZnYicSZui5G2eWe");

            string[] intervals = { "30m", "2h", "1d", "1w" };
            string[] intervalsForUpdate = { "5m", "30m", "1h", "1d" };
            var random = new Random();

            while (!cancelToken.IsCancellationRequested)
            {
                var ticker = await DataFindService.GetRandomTicker(random);

                string symbol = ticker.Symbol; // или BTCUSDT, ETHUSDT и т.д.

                Console.WriteLine($"Выбран токен {symbol} с движением цены {ticker.PriceChangePercent}%.");

                bool checkData = await service.IsSymbolAvailableOnBinanceSpotAsync(symbol);
                if (!checkData)
                {
                    Console.WriteLine($"Данных о токене {symbol} на Binance не найдены. Подбор другого токена..");
                    await Task.Delay(750, cancelToken);
                    continue;
                }

                string json = await GetDataAboutTicket(symbol, service, intervals);

                var balance = await GetBalance(bybitService);

                var signal = await PromtSender.GetSignalDataFromLLM(ticker, json, balance);
                if (signal.InvestmentAmountUsd < 2)
                {
                    Console.WriteLine($"LLM решила не вкладываться в {symbol}. Поиск следующего токена..");
                    await Task.Delay(750, cancelToken);
                    continue;
                }

                Console.WriteLine("Начало работы по сигналу..");

                await AddOrder(bybitService, signal, symbol);

                bool exitFromWhile = true;
                int indexPos = 0;

                while (exitFromWhile) 
                {
                    await Task.Delay(15 * 60 * 1000, cancelToken);

                    int actualContract = await CheckPositionAndOrder(bybitService, symbol, signal); // 0 - сделка закрыта, 1 - открыт ордер, 2 - открыта позиция

                    if (actualContract == 0)
                    {
                        Console.WriteLine("Сделка успешно закрыта.");
                        break;
                    }
                    indexPos++;

                    if (indexPos > 2)
                        continue;

                    string newJson = await GetDataAboutTicket(symbol, service, intervalsForUpdate);

                    balance = await GetBalance(bybitService);

                    var updatedSignal = await PromtSender.UpdateSignalFromLLM(ticker, newJson, signal, balance);

                    if(updatedSignal.Action.ToLower() != signal.Action.ToLower() && updatedSignal.Action.ToLower() != "hold")
                    {
                        Console.WriteLine("Решение по сделке изменено. Но пока не предпринимаем никаких действий");
                        continue;
                    }

                    if(actualContract == 2)
                    {
                        await bybitService.UpdatePositionTpSlAsync(symbol, updatedSignal.TakeProfit, updatedSignal.StopLoss);
                    }
                }
            }            
        }
        private async Task<decimal> GetBalance(BybitService bybitService)
        {
            var balance = await bybitService.GetUsdtBalanceAsync();

            Console.WriteLine($"Текущий баланс кошелька: {balance} USDT.");

            return balance;
        }
        private async Task<JsonDocument> AddOrder(BybitService bybitService, TradingSignal signal, string symbol)
        {
            string action = "Buy";

            if (signal.Action.ToLower() == "sell" || signal.Action.ToLower() == "short")
                action = "Sell";

            int leverage = 10;
            if (signal.SuccessProbability > 85)
                leverage = 20;

            Console.WriteLine($"Открытие ордера {symbol}.\n\tEntry = {signal.EntryPrice}.\n\tTP = {signal.TakeProfit}.\n\tSL = {signal.StopLoss}..");

            var retReq = await bybitService.PlaceConditionalTradeAsync(symbol.ToUpper(), signal.EntryPrice, signal.InvestmentAmountUsd, signal.TakeProfit, signal.StopLoss, leverage, action);

            if (retReq != null)
            {
                Console.WriteLine("Сделка успешно создана!");
            }

            return retReq;
        }

        private async Task<int> CheckPositionAndOrder(BybitService bbs, string symbol, TradingSignal signal)
        {
            Console.WriteLine("Проверка ордера / позиции..");

            var position = await bbs.GetOpenPositionAsync(symbol);

            string action = "Buy";

            if (signal.Action.ToLower() == "sell" || signal.Action.ToLower() == "short")
                action = "Sell";

            var activeOrders = await bbs.GetActiveOrderAsync(symbol, action);

            if (position == null && activeOrders == null)
                return 0;

            if (activeOrders == null)
                return 2; // Позиция открыта


            return 1; // Открыт только ордер
        }
        private async Task<string> GetDataAboutTicket(string symbol, BinanceService service, string[] intervals)
        {
            Console.WriteLine($"Получение данных для {symbol}...");
            var snapshot = await service.GetMarketSnapshotAsync(symbol, intervals);

            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            Console.WriteLine("Данные о токене получены..");
            return json;
        }
    }
}
