'use strict';

// 期交所 MIS 期貨即時報價 + 當日一分鐘 K。
// 對應 C# 版 Services\TaifexQuoteSource.cs 與 Services\TaifexIntradaySource.cs。
//
// 關鍵坑(照搬 C# 的處理):
//   - 送出的 body 欄位必須是 PascalCase。若被序列化成 cID,期交所會忽略商品別,
//     一律回台指期清單 —— 表面有資料、值卻是錯的。JSON.stringify 保留原始 key,沒問題。
//   - QuoteList 第一列是現貨(-S),不是期貨;清單裡混著週契約,要取「代號以 CID 開頭」
//     的第一口才是標準月契約近月。
//   - getChartData1M 的 SymbolID 要傳字串,傳陣列會 400。

const { USER_AGENT, num, timeFromHHmmss } = require('./util');

const QUOTE_LIST_URL = 'https://mis.taifex.com.tw/futures/api/getQuoteList';
const CHART_URL = 'https://mis.taifex.com.tw/futures/api/getChartData1M';
const REFERER =
  'https://mis.taifex.com.tw/futures/RegularSession/EquityIndices/FuturesDomestic/';

// 使用者輸入的代號 → 期交所商品代碼(CID)。
const CID_ALIASES = {
  TX: 'TXF', // 臺股期貨
  MTX: 'MXF', // 小型臺指期貨
  TMF: 'TMF', // 微型臺指期貨
  TE: 'EXF', // 電子期貨
  TF: 'FXF', // 金融期貨
  T5F: 'T5F', // 臺灣50期貨
};

function headers(json) {
  return {
    'User-Agent': USER_AGENT,
    Accept: 'application/json, text/plain, */*',
    Referer: REFERER,
    Origin: 'https://mis.taifex.com.tw',
    ...(json ? { 'Content-Type': 'application/json' } : {}),
  };
}

function canHandle(symbol) {
  return symbol.kind === 'future';
}

function resolveCid(symbol) {
  const upper = symbol.code.toUpperCase();
  return CID_ALIASES[upper] || upper;
}

async function getQuotes(symbols, signal) {
  const targets = symbols.filter(canHandle);
  if (targets.length === 0) return [];

  // 同一個 CID 只查一次,多檔共用結果。
  const groups = new Map();
  for (const s of targets) {
    const cid = resolveCid(s);
    if (!groups.has(cid)) groups.set(cid, []);
    groups.get(cid).push(s);
  }

  const quotes = [];
  for (const [cid, members] of groups) {
    const rows = await fetchProduct(cid, signal);
    for (const symbol of members) {
      const row = pickContract(rows, cid, symbol.code);
      if (row) quotes.push(toQuote(row, symbol));
    }
  }
  return quotes;
}

async function fetchProduct(cid, signal) {
  const payload = {
    MarketType: '0',
    SymbolType: 'F',
    KindID: '1',
    CID: cid,
    ExpireMonth: '',
    RowSize: '100',
    PageNo: '',
    SortColumn: '',
    SortOrder: '',
  };

  const res = await fetch(QUOTE_LIST_URL, {
    method: 'POST',
    headers: headers(true),
    body: JSON.stringify(payload),
    signal,
  });
  if (!res.ok) throw new Error(`期交所 HTTP ${res.status}`);
  const doc = await res.json();

  const code = doc && doc.RtCode != null ? String(doc.RtCode) : null;
  if (code && code !== '0' && code !== '0000') {
    throw new Error(`期交所回應錯誤 RtCode=${code} ${(doc && doc.RtMsg) || ''}`.trim());
  }

  const list = doc && doc.RtData && doc.RtData.QuoteList;
  return Array.isArray(list) ? list : [];
}

// 選出要顯示的合約。第一筆是現貨(-S),之後由近而遠,中間可能夾週契約。
// 使用者若直接輸入完整合約代號(例如 TXFI6)就用那一口,否則取「代號以 CID 開頭」的
// 第一口,也就是標準月契約近月。
function pickContract(rows, cid, code) {
  let monthly = null;
  let anyFuture = null;
  const upperCode = code.toUpperCase();
  const upperCid = cid.toUpperCase();

  for (const row of rows) {
    const symbolId = String(row.SymbolID || '');
    if (symbolId.includes('/')) continue; // 跨月價差組合單

    const contract = symbolId.split('-')[0];
    if (contract.toUpperCase() === upperCode) return row;

    if (!symbolId.toUpperCase().endsWith('-F')) continue;

    if (!anyFuture) anyFuture = row;
    if (!monthly && contract.toUpperCase().startsWith(upperCid)) monthly = row;
  }
  return monthly || anyFuture;
}

function toQuote(row, symbol) {
  const bid = num(row.CBidPrice1) ?? num(row.CBestBidPrice);
  const ask = num(row.CAskPrice1) ?? num(row.CBestAskPrice);

  // CRefPrice 是前一交易日結算價,期交所的漲跌 CDiff 就以它為基準。
  let prevClose = num(row.CRefPrice) ?? num(row.SettlementPrice);
  const last = num(row.CLastPrice) ?? bid ?? ask;

  if (prevClose === null && last !== null) {
    const diff = num(row.CDiff);
    if (diff !== null) prevClose = last - diff;
  }

  const symbolId = row.SymbolID ? String(row.SymbolID) : null;

  return {
    key: symbol.key,
    code: symbol.code,
    name: symbol.displayName || row.DispCName || symbolId || null,
    contract: symbolId ? symbolId.split('-')[0] : null,
    last,
    open: num(row.COpenPrice),
    high: num(row.CHighPrice),
    low: num(row.CLowPrice),
    prevClose,
    bid,
    ask,
    volume: num(row.CTotalVolume),
    tradeTime: timeFromHHmmss(row.CTime),
  };
}

// ── 當日一分鐘 K(走勢線底線) ─────────────────────────────
async function getIntraday(symbol, contract, signal) {
  if (!contract) return [];

  const res = await fetch(CHART_URL, {
    method: 'POST',
    headers: headers(true),
    body: JSON.stringify({ SymbolID: `${contract}-F` }),
    signal,
  });
  if (!res.ok) throw new Error(`期交所 K 線 HTTP ${res.status}`);
  const doc = await res.json();

  const ticks = doc && doc.RtData && doc.RtData.Ticks;
  if (!Array.isArray(ticks)) return [];

  const points = [];
  for (const tick of ticks) {
    // 每筆 [HHmmss, 開, 高, 低, 收, 量],取收盤價當走勢點。
    if (!Array.isArray(tick) || tick.length < 5) continue;
    const t = timeFromHHmmss(tick[0]);
    const price = num(tick[4]);
    if (t !== null && price !== null) points.push({ t, price });
  }
  return points;
}

module.exports = { canHandle, getQuotes, getIntraday };
