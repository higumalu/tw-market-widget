'use strict';

// 證交所 MIS 即時報價(上市／上櫃個股與指數)。
// 端點:GET https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_2330.tw|otc_6488.tw&json=1&delay=0
// 對應 C# 版 Services\TwseQuoteSource.cs。
//
// 說明:index.jsp 目前回 404,但報價 API 照樣能用,所以取 session cookie 這步失敗
// 不擋後續查詢(與 C# 的 EnsureSessionAsync 一致)。

const { USER_AGENT, num, firstPrice } = require('./util');

const INDEX_URL = 'https://mis.twse.com.tw/stock/index.jsp';
const API_URL = 'https://mis.twse.com.tw/stock/api/getStockInfo.jsp';
const BATCH_SIZE = 40; // MIS 單次查詢的檔數上限,超過就分批。

let sessionCookie = null;
let sessionReady = false;

function canHandle(symbol) {
  return symbol.kind === 'stock' || symbol.kind === 'index';
}

async function ensureSession() {
  if (sessionReady) return;
  try {
    const res = await fetch(INDEX_URL, {
      headers: { 'User-Agent': USER_AGENT, Referer: INDEX_URL },
    });
    const setCookie =
      typeof res.headers.getSetCookie === 'function' ? res.headers.getSetCookie() : [];
    if (setCookie.length > 0) {
      sessionCookie = setCookie.map((c) => c.split(';')[0]).join('; ');
    }
  } catch {
    // index.jsp 偶爾 404 或連不上,不影響報價 API。
  }
  sessionReady = true;
}

function baseHeaders() {
  const h = {
    'User-Agent': USER_AGENT,
    Accept: 'application/json, text/plain, */*',
    'Accept-Language': 'zh-TW,zh;q=0.9,en;q=0.8',
    Referer: INDEX_URL,
  };
  if (sessionCookie) h.Cookie = sessionCookie;
  return h;
}

function chunk(arr, size) {
  const out = [];
  for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
  return out;
}

async function getQuotes(symbols, signal) {
  const targets = symbols.filter(canHandle);
  if (targets.length === 0) return [];

  await ensureSession();

  const quotes = [];
  for (const batch of chunk(targets, BATCH_SIZE)) {
    quotes.push(...(await fetchBatch(batch, signal)));
  }
  return quotes;
}

async function fetchBatch(batch, signal) {
  const exCh = batch.map((s) => s.exCh).join('|');
  const stamp = Date.now();
  const url = `${API_URL}?ex_ch=${encodeURIComponent(exCh)}&json=1&delay=0&_=${stamp}`;

  const res = await fetch(url, { headers: baseHeaders(), signal });
  if (!res.ok) throw new Error(`證交所 HTTP ${res.status}`);
  const doc = await res.json();

  const code = doc && doc.rtcode;
  if (code && code !== '0000') {
    // session 過期時 MIS 會回非 0000,下一輪重新取 cookie。
    sessionReady = false;
    sessionCookie = null;
    throw new Error(`證交所回應錯誤 rtcode=${code} ${doc.rtmessage || ''}`.trim());
  }

  const array = Array.isArray(doc && doc.msgArray) ? doc.msgArray : [];
  const byExCh = new Map(batch.map((s) => [s.exCh.toLowerCase(), s]));
  const quotes = [];

  for (const item of array) {
    const itemCode = item.c;
    const exchange = item.ex;
    if (!itemCode || !exchange) continue;
    const symbol = byExCh.get(`${exchange}_${itemCode}.tw`.toLowerCase());
    if (!symbol) continue;
    quotes.push(toQuote(item, symbol));
  }
  return quotes;
}

function toQuote(item, symbol) {
  const bid = firstPrice(item.b);
  const ask = firstPrice(item.a);
  // 無成交時 z 會是 "-",用委買價遞補,仍沒有就退回開盤價。
  const last = num(item.z) ?? bid ?? ask ?? num(item.o);

  return {
    key: symbol.key,
    code: symbol.code,
    name: symbol.displayName || item.n || item.nf || null,
    contract: null,
    last,
    open: num(item.o),
    high: num(item.h),
    low: num(item.l),
    prevClose: num(item.y),
    bid,
    ask,
    volume: num(item.v),
    tradeTime: tradeTime(item),
  };
}

function tradeTime(item) {
  if (item.tlong) {
    const ms = Number(item.tlong);
    if (Number.isFinite(ms) && ms > 0) return ms;
  }
  if (item.t) {
    const parts = String(item.t).split(':').map(Number);
    if (parts.length >= 2 && parts.every((n) => Number.isFinite(n))) {
      const d = new Date();
      d.setHours(parts[0], parts[1], parts[2] || 0, 0);
      return d.getTime();
    }
  }
  return null;
}

module.exports = { canHandle, getQuotes };
