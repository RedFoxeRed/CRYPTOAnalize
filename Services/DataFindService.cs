using CryptoAnalyzer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CRYPTOAnalize.Services
{
    public static class DataFindService
    {
        public static async Task<Ticker24hr> GetRandomTicker(Random random)
        {
            var bbService = new BybitService("ss", "ss");
            var topDriveSignals = await bbService.GetTop10GainersAndLosersWith25WeeksAsync();
            //string topDriveSignalsString = "";
            //topDriveSignals.TopGainers.ForEach(x => topDriveSignalsString += x.Symbol + ": " + x.PriceChangePercent + "; ");
            //Console.WriteLine(topDriveSignalsString);

            int UpOrLose = random.Next(0, 1);

            var workList = topDriveSignals.TopGainers;
            if (UpOrLose == 0)
                workList = topDriveSignals.TopLosers;

            int randIndexSymbol = random.Next(0, workList.Count - 1);

            return workList[randIndexSymbol];
        }
    }
}
