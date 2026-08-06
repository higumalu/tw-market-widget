'use strict';

// 報價／走勢的聚合層,對應 C# 版 Services\QuoteService.cs 與 IntradayService.cs。
// 把自選清單分派給各來源,把結果合起來;任何一個來源掛掉都不會讓整批失敗。

const twse = require('./twse');
const taifex = require('./taifex');
const yahoo = require('./yahoo');

const QUOTE_SOURCES = [twse, taifex];
const INTRADAY_SOURCES = [yahoo, taifex];

async function getQuotes(symbols, signal) {
  const jobs = QUOTE_SOURCES.map((source) => ({
    source,
    symbols: symbols.filter((s) => source.canHandle(s)),
  })).filter((j) => j.symbols.length > 0);

  const results = await Promise.all(
    jobs.map(async (j) => {
      try {
        return { quotes: await j.source.getQuotes(j.symbols, signal), error: null };
      } catch (err) {
        if (err && err.name === 'AbortError') throw err;
        return { quotes: [], error: err && err.message ? err.message : String(err) };
      }
    })
  );

  return {
    quotes: results.flatMap((r) => r.quotes),
    errors: results.map((r) => r.error).filter((e) => e),
  };
}

async function getIntraday(symbol, contract, signal) {
  const source = INTRADAY_SOURCES.find((s) => s.canHandle(symbol));
  if (!source) return [];
  try {
    return await source.getIntraday(symbol, contract, signal);
  } catch (err) {
    if (err && err.name === 'AbortError') throw err;
    // 底線抓不到不影響即時報價,走勢就從現在開始畫。
    return [];
  }
}

module.exports = { getQuotes, getIntraday };
