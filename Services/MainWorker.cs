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

            string[] intervals = { "30m", "2h", "1d", "1w" };
            string[] intervalsForUpdate = { "5m", "30m", "1h", "1d" };
            var random = new Random();

            while (!cancelToken.IsCancellationRequested)
            {
                var bybitService = new BybitService("vsJFlH9X3SGalNFVci", "Zut5UqZCzUc4iHhDb6X6MxgdiPRXNFnaL1F2");

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
                if (balance < 20)
                {
                    Console.WriteLine("Денег мало. Работа программы остановлена");
                    break;
                }

                var signal = await PromtSender.GetSignalDataFromLLM(ticker, json, balance);

                if (signal == null)
                    continue;

                if (signal.InvestmentAmountUsd < 2)
                {
                    Console.WriteLine($"Было решено не вкладываться в {symbol}. Поиск следующего токена..");
                    await Task.Delay(750, cancelToken);
                    continue;
                }

                Console.WriteLine("Начало работы по сигналу..");
                Console.WriteLine($"Попытка вложиться на {signal.InvestmentAmountUsd} USDT..");

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
                        await GetBalance(bybitService);
                        break;
                    }
                    indexPos++;

                    if (indexPos > 2)
                        continue;

                    string newJson = await GetDataAboutTicket(symbol, service, intervalsForUpdate);

                    balance = await GetBalance(bybitService);

                    //var updatedSignal = await PromtSender.UpdateSignalFromLLM(ticker, newJson, signal, balance);

                    //if(updatedSignal.Action.ToLower() != signal.Action.ToLower() && updatedSignal.Action.ToLower() != "hold")
                    //{
                    //    Console.WriteLine("Решение по сделке изменено. Но пока не предпринимаем никаких действий");
                    //    continue;
                    //}

                    //if(actualContract == 2)
                    //{
                    //    Console.WriteLine($"Принято решение изменить данные позиции:\n\tTP: {signal.TakeProfit} -> {updatedSignal.TakeProfit}\n\tSL: {signal.StopLoss} -> {updatedSignal.StopLoss}");
                    //    await bybitService.UpdatePositionTpSlAsync(symbol, updatedSignal.TakeProfit, updatedSignal.StopLoss);
                    //}
                }
            }            
        }
        private async Task<decimal> GetBalance(BybitService bybitService)
        {
            decimal balance = await bybitService.GetUsdtBalanceAsync();

            Console.WriteLine($"Текущий баланс кошелька: {balance} USDT.");

            return balance;
        }
        private async Task<JsonDocument> AddOrder(BybitService bybitService, TradingSignal signal, string symbol)
        {
            string side = signal.Action.ToLower() switch
            {
                "sell" or "short" => "Sell",
                _ => "Buy"
            };

            int leverage = signal.SuccessProbability > 85 ? 20 : 10;

            Console.WriteLine($"Открытие ордера {symbol}.\n\tEntry = {signal.EntryPrice}\n\tTP = {signal.TakeProfit}\n\tSL = {signal.StopLoss}");

            var response = await bybitService.PlaceConditionalTradeAsync(
                symbol,
                signal.EntryPrice,
                signal.InvestmentAmountUsd * leverage,
                signal.TakeProfit,
                signal.StopLoss,
                leverage,
                side
            );

            Console.WriteLine("Сделка успешно создана!");
            return response;
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
