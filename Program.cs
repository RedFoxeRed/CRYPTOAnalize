using System;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using CRYPTOAnalize.Services;
using CryptoAnalyzer.Models;
using CryptoAnalyzer.Services;
using Newtonsoft.Json;

var worker = new MainWorker();
var cts = new CancellationTokenSource();
var workerTask = worker.StartToWorkAsync(cts.Token);

Console.WriteLine("Программа работает. Нажмите любую клавишу для остановки...");
_ = Console.ReadKey(intercept: true);

cts.Cancel();

try
{
    await workerTask; // Дожидаемся корректного завершения
}
catch (OperationCanceledException)
{
    Console.WriteLine("Программа остановлена по запросу.");
}

Console.WriteLine("Готово.");
