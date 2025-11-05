using System;
using System.Threading.Tasks;
using CryptoAnalyzer.Models;
using CryptoAnalyzer.Services;
using Newtonsoft.Json;

var service = new BinanceService();
string symbol = "XRPUSDT"; // или BTCUSDT, ETHUSDT и т.д.

string[] intervals = { "30m", "2h", "1d", "1w" };

Console.WriteLine($"Получение данных для {symbol}...");
var snapshot = await service.GetMarketSnapshotAsync(symbol, intervals);

string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
Console.WriteLine(json);