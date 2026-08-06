// 渲染層主控。對應 C# 版 ViewModels\MainViewModel + QuoteRowViewModel + MainWindow。
import { fmtNumber, fmtSigned, dirClass } from './format.js';
import { makeSymbol, parseInput, kindLabel, sessionStart, sessionEnd } from './symbols.js';
import { drawSparkline } from './sparkline.js';

const MAX_POINTS = 6000; // 開盤起每 5 秒一點約 3200 點,留餘裕。

const todayStr = () => new Date().toDateString();
const clamp = (v, lo, hi) => Math.min(Math.max(v, lo), hi);
const hhmmss = (d) =>
  [d.getHours(), d.getMinutes(), d.getSeconds()].map((n) => String(n).padStart(2, '0')).join(':');

// ── 一列報價(物件重複使用,只換值) ─────────────────────────
class Row {
  constructor(symbol) {
    this.symbol = symbol;
    this.quote = null;
    this.isStale = false;
    this.series = [];
    this.seededOn = null;
    this.dom = null; // 建構結構時掛上元素參考
  }

  get key() {
    return this.symbol.key;
  }
  get code() {
    return this.symbol.code;
  }
  get name() {
    return this.symbol.displayName || (this.quote && this.quote.name) || this.symbol.code;
  }
  get contract() {
    return this.quote && this.quote.contract;
  }
  get hasData() {
    return this.quote !== null;
  }
  get last() {
    return this.quote ? this.quote.last : null;
  }
  get prevClose() {
    return this.quote ? this.quote.prevClose : null;
  }
  get change() {
    const q = this.quote;
    return q && q.last != null && q.prevClose != null ? q.last - q.prevClose : null;
  }
  get changePercent() {
    const q = this.quote;
    return q && q.last != null && q.prevClose != null && q.prevClose !== 0
      ? ((q.last - q.prevClose) / q.prevClose) * 100
      : null;
  }
  get direction() {
    const c = this.change;
    if (c === null) return 0;
    return c > 0 ? 1 : c < 0 ? -1 : 0;
  }
  get tooltip() {
    return this.contract ? `${this.code} ${this.name}（${this.contract}）` : `${this.code} ${this.name}`;
  }

  update(quote) {
    this.quote = quote;
    this.isStale = false;
    if (quote.last != null) this.append({ t: quote.tradeTime ?? Date.now(), price: quote.last });
  }

  // 用開盤到現在的資料鋪底,接在既有的即時點之前(對應 QuoteRowViewModel.Seed)。
  seed(history) {
    this.seededOn = todayStr();
    if (!history || history.length === 0) return;
    const live = this.series.slice();
    const lastSeeded = history[history.length - 1].t;
    this.series = history.slice();
    for (const p of live) if (p.t > lastSeeded) this.series.push(p);
    this._trim();
  }

  append(point) {
    const s = this.series;
    if (s.length > 0 && s[s.length - 1].t >= point.t) {
      // 同一秒重複回報就只更新最後一點,不要一直長。
      if (s[s.length - 1].price === point.price) return;
      s[s.length - 1] = { t: s[s.length - 1].t, price: point.price };
    } else {
      s.push(point);
    }
    this._trim();
  }

  _trim() {
    if (this.series.length > MAX_POINTS) this.series.splice(0, this.series.length - MAX_POINTS);
  }
}

// ── 狀態 ─────────────────────────────────────────────────
let settings = null;
let rows = [];
let selectedKey = null;
let busy = false;
let timer = null;
let fullWidth = 1240;
let fullHeight = 520;
const seeding = new Set();

const $ = (id) => document.getElementById(id);
const els = {};

// ── 啟動 ─────────────────────────────────────────────────
async function init() {
  settings = await window.api.loadSettings();
  fullWidth = settings.windowWidth;
  fullHeight = settings.windowHeight;

  rows = settings.watchlist.map((w) => new Row(makeSymbol(w)));

  cacheEls();
  applyControlsFromSettings();
  wireEvents();
  renderStructure();
  applyMode(settings.compactMode, true);

  startTimer();
  refresh();
}

function cacheEls() {
  for (const id of [
    'titlebar', 'btn-mode', 'btn-min', 'btn-close', 'opacity', 'opacity-label',
    'refresh', 'ontop', 'btn-refresh', 'compact', 'tbody', 'new-code', 'new-market',
    'btn-add', 'btn-remove', 'btn-up', 'btn-down', 'status', 'error', 'busy',
  ]) {
    els[id] = $(id);
  }
}

function applyControlsFromSettings() {
  document.documentElement.style.setProperty('--bg-opacity', settings.backgroundOpacity);
  els.opacity.value = settings.backgroundOpacity;
  els['opacity-label'].textContent = `${Math.round(settings.backgroundOpacity * 100)}%`;
  els.refresh.value = settings.refreshSeconds;
  els.ontop.checked = settings.alwaysOnTop;
  els['btn-mode'].textContent = settings.compactMode ? '完整' : '精簡';
}

// ── 事件 ─────────────────────────────────────────────────
function wireEvents() {
  els.titlebar.addEventListener('dblclick', () => window.api.toggleMaximize());
  els['btn-min'].addEventListener('click', () => window.api.minimize());
  els['btn-close'].addEventListener('click', () => window.api.close());
  els['btn-mode'].addEventListener('click', toggleMode);
  els['btn-refresh'].addEventListener('click', refresh);

  els.opacity.addEventListener('input', () => {
    settings.backgroundOpacity = clamp(Number(els.opacity.value), 0.15, 1);
    document.documentElement.style.setProperty('--bg-opacity', settings.backgroundOpacity);
    els['opacity-label'].textContent = `${Math.round(settings.backgroundOpacity * 100)}%`;
    save();
  });

  els.refresh.addEventListener('change', () => {
    settings.refreshSeconds = clamp(parseInt(els.refresh.value, 10) || 5, 3, 60);
    els.refresh.value = settings.refreshSeconds;
    startTimer();
    save();
  });

  els.ontop.addEventListener('change', () => {
    settings.alwaysOnTop = els.ontop.checked;
    window.api.setAlwaysOnTop(settings.alwaysOnTop);
    save();
  });

  els['btn-add'].addEventListener('click', addSymbol);
  els['new-code'].addEventListener('keydown', (e) => {
    if (e.key === 'Enter') addSymbol();
  });
  els['btn-remove'].addEventListener('click', removeSelected);
  els['btn-up'].addEventListener('click', () => move(-1));
  els['btn-down'].addEventListener('click', () => move(1));

  window.addEventListener('resize', () => {
    if (document.body.classList.contains('compact')) {
      settings.compactWidth = clamp(window.outerWidth, 320, 900);
      drawSparklines();
    } else {
      fullWidth = window.outerWidth;
      fullHeight = window.outerHeight;
    }
  });
}

// ── 精簡／完整模式 ────────────────────────────────────────
function toggleMode() {
  const next = !settings.compactMode;
  settings.compactMode = next;
  els['btn-mode'].textContent = next ? '完整' : '精簡';
  save();
  applyMode(next);
}

function applyMode(compact, initial = false) {
  document.body.classList.toggle('compact', compact);
  document.body.classList.toggle('full', !compact);
  window.api.setMode(compact);

  if (compact) {
    renderValues();
    // 高度跟著列數自動縮(對應 WPF 的 SizeToContent=Height)。
    requestAnimationFrame(() => {
      const h = document.querySelector('.app').offsetHeight + 8;
      window.api.resizeTo(settings.compactWidth, h);
      requestAnimationFrame(drawSparklines);
    });
  } else if (!initial) {
    window.api.resizeTo(fullWidth, fullHeight);
    renderValues();
  }
}

// ── 輪詢 ─────────────────────────────────────────────────
function startTimer() {
  if (timer) clearInterval(timer);
  timer = setInterval(refresh, settings.refreshSeconds * 1000);
}

async function refresh() {
  if (busy) return;
  const symbols = rows.map((r) => r.symbol);
  if (symbols.length === 0) {
    els.status.textContent = '自選清單是空的';
    return;
  }

  busy = true;
  els.busy.style.display = 'block';
  try {
    const { quotes, errors } = await window.api.fetchQuotes(symbols);
    const byKey = new Map(quotes.map((q) => [String(q.key).toLowerCase(), q]));

    let hit = 0;
    for (const r of rows) {
      const q = byKey.get(r.key.toLowerCase());
      if (q) {
        r.update(q);
        hit++;
      } else {
        r.isStale = true;
      }
    }

    els.status.textContent = `更新於 ${hhmmss(new Date())}（${hit}/${rows.length} 檔）`;
    els.error.textContent = errors && errors.length ? [...new Set(errors)].join('；') : '';
    renderValues();
    seedPending();
  } catch (e) {
    els.error.textContent = e && e.message ? e.message : String(e);
  } finally {
    busy = false;
    els.busy.style.display = 'none';
  }
}

// 幫還沒有底線的商品補「當日開盤到現在」的走勢。期貨要等第一次報價回來、
// 知道近月合約是哪一口之後才抓得動,所以放在每次更新之後檢查。
function seedPending() {
  for (const r of rows) {
    if (r.seededOn === todayStr()) continue;
    if (!r.hasData) continue;
    if (r.symbol.kind === 'future' && !r.contract) continue;
    if (seeding.has(r.key)) continue;

    seeding.add(r.key);
    (async () => {
      try {
        const history = await window.api.fetchIntraday(r.symbol, r.contract);
        r.seed(history);
        drawSparkFor(r);
      } finally {
        seeding.delete(r.key);
      }
    })();
  }
}

// ── 自選清單編輯 ──────────────────────────────────────────
function addSymbol() {
  const symbol = parseInput(els['new-code'].value, els['new-market'].value);
  if (!symbol) return;

  if (rows.some((r) => r.key.toLowerCase() === symbol.key.toLowerCase())) {
    els.error.textContent = `${symbol.code} 已在清單中`;
    return;
  }
  rows.push(new Row(symbol));
  els['new-code'].value = '';
  save();
  renderStructure();
  refresh();
}

function removeSelected() {
  const idx = rows.findIndex((r) => r.key === selectedKey);
  if (idx < 0) return;
  rows.splice(idx, 1);
  selectedKey = null;
  save();
  renderStructure();
}

function move(offset) {
  const idx = rows.findIndex((r) => r.key === selectedKey);
  const target = idx + offset;
  if (idx < 0 || target < 0 || target >= rows.length) return;
  [rows[idx], rows[target]] = [rows[target], rows[idx]];
  save();
  renderStructure();
}

function save() {
  settings.watchlist = rows.map((r) => ({
    code: r.symbol.code,
    kind: r.symbol.kind,
    market: r.symbol.market,
    displayName: r.symbol.displayName,
  }));
  window.api.saveSettings(settings);
}

// ── 結構渲染(新增／刪除／搬移時重建;輪詢時只換值,不動結構) ──
function renderStructure() {
  els.tbody.replaceChildren();
  els.compact.replaceChildren();

  for (const r of rows) {
    r.dom = { tr: buildTableRow(r), compact: buildCompactRow(r) };
    els.tbody.appendChild(r.dom.tr.el);
    els.compact.appendChild(r.dom.compact.el);
  }
  renderValues();
  if (document.body.classList.contains('compact')) {
    requestAnimationFrame(() => {
      const h = document.querySelector('.app').offsetHeight + 8;
      window.api.resizeTo(settings.compactWidth, h);
      drawSparklines();
    });
  }
}

const NUM_COLS = ['last', 'change', 'pct', 'bid', 'ask', 'open', 'high', 'low', 'prev', 'vol', 'time'];

function buildTableRow(r) {
  const tr = document.createElement('tr');
  tr.dataset.key = r.key;
  tr.addEventListener('click', () => selectRow(r.key));

  const cells = {};
  const add = (cls) => {
    const td = document.createElement('td');
    td.className = cls;
    tr.appendChild(td);
    return td;
  };
  cells.kind = add('cell-kind');
  cells.code = add('cell-text');
  cells.name = add('cell-text trim');
  cells.last = add('cell-num bold');
  cells.change = add('cell-num');
  cells.pct = add('cell-num');
  cells.bid = add('cell-num');
  cells.ask = add('cell-num');
  cells.open = add('cell-num');
  cells.high = add('cell-num');
  cells.low = add('cell-num');
  cells.prev = add('cell-num cell-muted');
  cells.vol = add('cell-num');
  cells.time = add('cell-num cell-muted');
  return { el: tr, cells };
}

function buildCompactRow(r) {
  const div = document.createElement('div');
  div.className = 'compact-row';
  div.dataset.key = r.key;
  div.addEventListener('click', () => selectRow(r.key));

  const code = document.createElement('span');
  code.className = 'c-code';
  const name = document.createElement('span');
  name.className = 'c-name';
  const canvas = document.createElement('canvas');
  canvas.className = 'c-spark';
  const last = document.createElement('span');
  last.className = 'c-last';
  const pct = document.createElement('span');
  pct.className = 'c-pct';

  div.append(code, name, canvas, last, pct);
  return { el: div, code, name, canvas, last, pct };
}

function selectRow(key) {
  selectedKey = key;
  for (const r of rows) {
    if (!r.dom) continue;
    r.dom.tr.el.classList.toggle('selected', r.key === key);
    r.dom.compact.el.classList.toggle('selected', r.key === key);
  }
}

// ── 值渲染(不動結構,避免閃爍) ──────────────────────────
function renderValues() {
  const compactVisible = document.body.classList.contains('compact');
  for (const r of rows) {
    if (!r.dom) continue;
    updateTableRow(r);
    updateCompactRow(r);
  }
  if (compactVisible) drawSparklines();
}

function setColored(td, text, direction) {
  td.textContent = text;
  td.classList.remove('up', 'down', 'flat');
  td.classList.add(dirClass(direction));
}

function updateTableRow(r) {
  const c = r.dom.tr.cells;
  r.dom.tr.el.classList.toggle('stale', r.isStale);

  c.kind.textContent = kindLabel(r.symbol);
  c.code.textContent = r.code;
  c.name.textContent = r.name;
  c.name.title = r.tooltip;

  setColored(c.last, fmtNumber(r.last, '0.00'), r.direction);
  setColored(c.change, fmtSigned(r.change, '0.00'), r.direction);
  setColored(c.pct, fmtSigned(r.changePercent, '0.00'), r.direction);

  const q = r.quote || {};
  c.bid.textContent = fmtNumber(q.bid, '0.00');
  c.ask.textContent = fmtNumber(q.ask, '0.00');
  c.open.textContent = fmtNumber(q.open, '0.00');
  c.high.textContent = fmtNumber(q.high, '0.00');
  c.low.textContent = fmtNumber(q.low, '0.00');
  c.prev.textContent = fmtNumber(q.prevClose, '0.00');
  c.vol.textContent = fmtNumber(q.volume, '#,##0');
  c.time.textContent = q.tradeTime ? hhmmss(new Date(q.tradeTime)) : '--:--:--';
}

function updateCompactRow(r) {
  const c = r.dom.compact;
  c.el.classList.toggle('stale', r.isStale);
  c.code.textContent = r.code;
  c.name.textContent = r.name;
  c.el.title = r.tooltip;

  const cls = dirClass(r.direction);
  c.last.textContent = fmtNumber(r.last, '0.00');
  c.last.className = `c-last ${cls}`;
  c.pct.textContent = fmtSigned(r.changePercent, '0.00|%');
  c.pct.className = `c-pct ${cls}`;
}

function drawSparklines() {
  for (const r of rows) drawSparkFor(r);
}

function drawSparkFor(r) {
  if (!r.dom || !document.body.classList.contains('compact')) return;
  drawSparkline(
    r.dom.compact.canvas,
    r.series,
    r.prevClose,
    sessionStart(r.symbol),
    sessionEnd(r.symbol)
  );
}

init();
