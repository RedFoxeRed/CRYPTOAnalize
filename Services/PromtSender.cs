using CryptoAnalyzer.Models;
using Newtonsoft.Json;
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
            string answer = "{\r\n  " +
                "\"prompt\": \"Ты — алгоритмический торговый движок с капиталом " + balance + " USDT, торгующий фьючерсами с плечом ×10. Тебе **запрещено выбирать 'hold'** — ты **всегда должен выбрать либо 'buy', либо 'sell'**, даже если сигналы слабые. Твоя задача — на основе **исключительно предоставленных данных** по " + ticker.Symbol + " оценить **краткосрочное движение цены в ближайшие 30 минут – 12 часов** " +
                "и выбрать направление с **наибольшей вероятностью успеха**.\\n\\n**Важно:**\\n- Ты не предсказываешь долгосрочные тренды — только ближайший импульс.\\n- Все решения основаны **только** на указанных индикаторах (RSI, MACD, объёмы, Bollinger) и ценах закрытия.\\n- Несмотря на отсутствие 'hold', ты **строго соблюдаешь риск-менеджмент**: размер позиции зависит от уверенности.\\n\\n" +
                "**Правила оценки:**\\n1. **Вычисли long_probability и short_probability (0.0–1.0)** на основе:\\n   - RSI(14) на 1d и 1w (перепроданность <40 → + к long; перекупленность >60 → + к short);\\n   - MACD histogram и пересечение линий на 1d;\\n   - Положение цены относительно Bollinger middle на 1d;\\n   - Направление импульса на 2h/30m (рост/падение RSI, MACD, цена выше/ниже локальных экстремумов);\\n" +
                "   - Соотношение buy_volume / total volume на 2h и 30m.\\n\\n2. **Выбор действия:**\\n   - Если long_probability ≥ short_probability → action = \\\"buy\\\";\\n   - Иначе → action = \\\"sell\\\".\\n\\n3. **Уверенность и риск:**\\n   - confidence = |long_probability - short_probability| + min(long_probability, short_probability) × 0.2;\\n " +
                "    (т.е. если 0.55 vs 0.45 → confidence ≈ 0.57; если 0.8 vs 0.2 → confidence ≈ 0.84)\\n   - success_probability = confidence;\\n   - investment_percent = \\n        - 0% если confidence < 0.5,\\n        - min(20%, (confidence - 0.5) * 40) если confidence ≥ 0.5;\\n   - investment_amount_usd = 10 000 × investment_percent / 100.\\n\\n" +
                "4. **Параметры сделки:**\\n   - entry_price = последнее close на 30m;\\n   - Для 'buy':\\n        stop_loss = min(low[2h][-3:]) или lower Bollinger(1d), в зависимости от того, что выше;\\n        take_profit = entry + 1.5 × (entry - stop_loss);\\n   - Для 'sell':\\n        stop_loss = max(high[2h][-3:]) или upper Bollinger(1d), в зависимости от того, что ниже;\\n" +
                "        take_profit = entry - 1.5 × (stop_loss - entry);\\n   - hold_time =\\n        - 90 мин, если разница вероятностей < 0.2 (слабый сигнал);\\n        - 180–360 мин, если разница ≥ 0.2;\\n        - 720 мин, если один из сигналов ≥ 0.8.\\n\\n**Вывод строго в формате JSON:**\\n{\\n  \\\"action\\\": \\\"buy|sell\\\",\\n  \\\"entry_price\\\": number,\\n" +
                "  \\\"stop_loss\\\": number,\\n  \\\"take_profit\\\": number,\\n  \\\"confidence\\\": number,\\n  \\\"success_probability\\\": number,\\n  \\\"long_probability\\\": number,\\n  \\\"short_probability\\\": number,\\n  \\\"investment_amount_usd\\\": number,\\n  \\\"investment_percent\\\": number,\\n  \\\"hold_time\\\": integer\\n}\\n" +
                "\\nНикаких 'hold', никаких пояснений — только JSON по схеме.\"\r\n}\r\ndata:" + json;

            string request = await AI_Responser.ASK(answer);
            TradingSignal retSig = null;
            try
            {
                retSig = JsonConvert.DeserializeObject<TradingSignal>(request.Replace("```json", "").Replace("```", "").Replace("'''", "").Trim());
            }
            catch
            {
                Console.WriteLine("Непредвиденая ошибка обработки запроса.. новая попытка");
            }

            return retSig;
        }

        public static async Task<TradingSignal> UpdateSignalFromLLM(Ticker24hr ticker, string newJsonData, TradingSignal LLM_signal, decimal balance)
        {
            string signalJSon = JsonConvert.SerializeObject(LLM_signal);

            string answer = "\r\n{\r\n  \"prompt\": \"Ты — алгоритмический торговый движок с капиталом " + balance + " USDT, торгующий фьючерсами с плечом ×10. Тебе даны:\\n\\n1. **Текущий активный сигнал** (действие, entry, старые TP/SL и т.д.);\\n2. **Обновлённые рыночные данные** по " + ticker.Symbol + " (RSI, MACD, объёмы, Bollinger, цены на 30m/2h/1d/1w).\\n\\n" +
                "Твоя задача — **пересчитать stop_loss и take_profit** на основе **новых данных**, сохранив:\\n- то же **action** (buy/sell),\\n- тот же **entry_price** (он фиксирован при открытии),\\n- все остальные параметры пересчитываются по тем же правилам, что и в исходном сигнале.\\n\\n**Правила пересчёта:**Несмотря на отсутствие 'hold', ты **строго соблюдаешь риск-менеджмент**: размер позиции зависит от уверенности.\\n" +
                "\\n**Правила оценки:**\\n1. **Вычисли long_probability и short_probability (0.0–1.0)** на основе:\\n   - RSI(14) на 1d и 1w (перепроданность <40 → + к long; перекупленность >60 → + к short);\\n   - MACD histogram и пересечение линий на 1d;\\n   - Положение цены относительно Bollinger middle на 1d;\\n   - Направление импульса на 2h/30m (рост/падение RSI, MACD, цена выше/ниже локальных экстремумов);\\n" +
                "   - Соотношение buy_volume / total volume на 2h и 30m.\\n\\n2. **Выбор действия:**\\n   - Если long_probability ≥ short_probability → action = \\\"buy\\\";\\n   - Иначе → action = \\\"sell\\\".\\n\\n3. **Уверенность и риск:**\\n   - confidence = |long_probability - short_probability| + min(long_probability, short_probability) × 0.2;\\n" +
                "     (т.е. если 0.55 vs 0.45 → confidence ≈ 0.57; если 0.8 vs 0.2 → confidence ≈ 0.84)\\n   - success_probability = confidence;\\n   - investment_percent = \\n        - 0% если confidence < 0.5,\\n        - min(20%, (confidence - 0.5) * 40) если confidence ≥ 0.5;\\n   - investment_amount_usd = 10 000 × investment_percent / 100.\\n\\n4. **Параметры сделки:**\\n " +
                "  - entry_price = последнее close на 30m;\\n   - Для 'buy':\\n        stop_loss = min(low[2h][-3:]) или lower Bollinger(1d), в зависимости от того, что выше;\\n        take_profit = entry + 1.5 × (entry - stop_loss);\\n   - Для 'sell':\\n        stop_loss = max(high[2h][-3:]) или upper Bollinger(1d), в зависимости от того, что ниже;\\n        take_profit = entry - 1.5 × (stop_loss - entry);\\n " +
                "  - hold_time =\\n        - 90 мин, если разница вероятностей < 0.2 (слабый сигнал);\\n        - 180–360 мин, если разница ≥ 0.2;\\n        - 720 мин, если один из сигналов ≥ 0.8\\n\\n В выходном JSON поменяй (Если считаешь нужным) только TakeProfit и StopLose. Остальные параметры (**confidence, success_probability, investment_amount_usd** и т.д.) **не меняй** — они привязаны к моменту входа.\\n\\n" +
                "**Вывод строго в том же JSON-формате, что и исходный сигнал**, с **обновлёнными только stop_loss и take_profit**.\\n\\nНикаких пояснений, только JSON.\"\r\n}\r\norderData:\r\n" + signalJSon + "\r\ndata:\r\n" + newJsonData;

            string request = await AI_Responser.ASK(answer);
            TradingSignal retSig = null;
            try
            {
                retSig = JsonConvert.DeserializeObject<TradingSignal>(request.Replace("```json", "").Replace("```", "").Replace("'''", "").Trim());
            }
            catch
            {
                Console.WriteLine("Непредвиденая ошибка обработки запроса.. новая попытка");
            }

            return retSig;
        }
    }
}
