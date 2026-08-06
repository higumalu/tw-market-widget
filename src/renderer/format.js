// 數值格式化,對應 C# 版 Converters\Converters.cs 的 NumberConverter / SignedNumberConverter。

// null → "—";'0.00' 兩位小數不分節;'#,##0' 整數分節;其餘退回精簡小數。
export function fmtNumber(value, format = '0.##') {
  if (value === null || value === undefined) return '—';
  const n = Number(value);
  if (!Number.isFinite(n)) return '—';
  if (format === '0.00') return n.toFixed(2);
  if (format === '#,##0') return Math.round(n).toLocaleString('en-US');
  return String(Number(n.toFixed(2)));
}

// 帶正負號,ConverterParameter 可寫 "0.00|%" 在後面接單位。
export function fmtSigned(value, param = '0.00') {
  if (value === null || value === undefined) return '—';
  const n = Number(value);
  if (!Number.isFinite(n)) return '—';
  const parts = param.split('|');
  const suffix = parts.length > 1 ? parts[1] : '';
  const text = fmtNumber(Math.abs(n), parts[0]) + suffix;
  if (n > 0) return `+${text}`;
  if (n < 0) return `-${text}`;
  return text;
}

// -1 跌、0 平、1 漲 → CSS class(台股習慣紅漲綠跌)。
export function dirClass(direction) {
  if (direction > 0) return 'up';
  if (direction < 0) return 'down';
  return 'flat';
}
