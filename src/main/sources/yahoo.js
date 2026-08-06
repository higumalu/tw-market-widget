'use strict';

// Yahoo Finance:個股與指數的當日分時線(走勢線底線)。
// 對應 C# 版 Services\YahooIntradaySource.cs。
// 證交所原本的 getChartInfo.jsp 已下架(404),找不到官方替代品,才用 Yahoo 補這段。
// Yahoo 台股分時延遲約 15～20 分鐘,所以只當底線,尾巴由證交所即時輪詢接上。

const { USER_AGENT, num } = require('./util');

const CHART_URL = 'https://query1.finance.yahoo.com/v8/finance/chart/';

function toYahooSymbol(symbol) {
  if (symbol.kind === 'stock') {
    return symbol.market && symbol.market.toLowerCase() === 'otc'
      ? `${symbol.code}.TWO`
      : `${symbol.code}.TW`;
  }
  if (symbol.kind === 'index') {
    switch (symbol.code.toLowerCase()) {
      case 't00':
        return '^TWII';
      case 'o00':
        return '^TWOII'; // 實測無分時資料,會回空
      default:
        return null;
    }
  }
  return null;
}

function canHandle(symbol) {
  return (
    (symbol.kind === 'stock' || symbol.kind === 'index') && toYahooSymbol(symbol) !== null
  );
}

async function getIntraday(symbol, _contract, signal) {
  const yahooSymbol = toYahooSymbol(symbol);
  if (!yahooSymbol) return [];

  const url = `${CHART_URL}${encodeURIComponent(yahooSymbol)}?interval=1m&range=1d`;
  const res = await fetch(url, {
    headers: { 'User-Agent': USER_AGENT, Accept: 'application/json' },
    signal,
  });
  if (!res.ok) throw new Error(`Yahoo HTTP ${res.status}`);
  const doc = await res.json();

  const result =
    doc && doc.chart && Array.isArray(doc.chart.result) && doc.chart.result[0];
  if (!result) return [];

  const timestamps = result.timestamp;
  const quote =
    result.indicators &&
    Array.isArray(result.indicators.quote) &&
    result.indicators.quote[0];
  const closes = quote && quote.close;
  if (!Array.isArray(timestamps) || !Array.isArray(closes)) return [];

  const count = Math.min(timestamps.length, closes.length);
  const points = [];
  for (let i = 0; i < count; i++) {
    const price = num(closes[i]); // 沒成交的分鐘 close 會是 null,跳過
    if (price === null) continue;
    points.push({ t: timestamps[i] * 1000, price });
  }
  return points;
}

module.exports = { canHandle, getIntraday };
