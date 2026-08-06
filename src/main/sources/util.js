'use strict';

// 各資料源共用的小工具。對應 C# 版 Services\ 裡重複出現的 Parse / FirstPrice / 時間解析。

const USER_AGENT =
  'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) ' +
  'Chrome/126.0.0.0 Safari/537.36';

// decimal.TryParse(NumberStyles.Any, InvariantCulture) 的等價:去逗號、"-" 視為無值。
function num(value) {
  if (value === null || value === undefined) return null;
  const s = String(value).trim();
  if (s === '' || s === '-') return null;
  const v = Number(s.replace(/,/g, ''));
  return Number.isFinite(v) ? v : null;
}

// 五檔欄位是 "580.0000_579.0000_..." 這種格式,取第一檔。
function firstPrice(packed) {
  if (!packed) return null;
  const first = String(packed).split('_').find((p) => p.trim() !== '');
  return num(first);
}

// HHmmss(可能帶毫秒)→ 當日的 epoch 毫秒。解析不出來回 null。
function timeFromHHmmss(hhmmss) {
  if (!hhmmss) return null;
  const digits = String(hhmmss).replace(/\D/g, '');
  if (digits.length < 6) return null;
  const h = Number(digits.slice(0, 2));
  const m = Number(digits.slice(2, 4));
  const s = Number(digits.slice(4, 6));
  if (h > 23 || m > 59 || s > 59) return null;
  const d = new Date();
  d.setHours(h, m, s, 0);
  return d.getTime();
}

module.exports = { USER_AGENT, num, firstPrice, timeFromHHmmss };
