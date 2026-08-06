// 自選商品的建立與衍生欄位,對應 C# 版 Models\WatchSymbol.cs 與 MainViewModel.AddSymbol。

// 建一個完整的商品物件;key / exCh 先算好,連同送去 main 給各資料源用。
export function makeSymbol({ code, kind, market = 'tse', displayName = null }) {
  const s = { code, kind, market, displayName };
  s.key = kind === 'future' ? `F:${code}` : `${market}:${code}`;
  s.exCh = `${market}_${code}.tw`;
  return s;
}

// 從輸入框(代號 + 市場別)解析成商品,對應 MainViewModel.AddSymbol。
export function parseInput(rawCode, newMarket) {
  const code = rawCode.trim().toUpperCase();
  if (code.length === 0) return null;

  if (newMarket === 'future') {
    return makeSymbol({ code, kind: 'future', market: 'tse' });
  }
  if (newMarket === 'index') {
    return makeSymbol({
      code: code.toLowerCase(),
      kind: 'index',
      market: code.startsWith('O') ? 'otc' : 'tse',
    });
  }
  return makeSymbol({ code, kind: 'stock', market: newMarket });
}

export function kindLabel(symbol) {
  switch (symbol.kind) {
    case 'stock':
      return symbol.market && symbol.market.toLowerCase() === 'otc' ? '上櫃' : '上市';
    case 'index':
      return '指數';
    case 'future':
      return '期貨';
    default:
      return '';
  }
}

// 走勢線 X 軸的當日開收盤(回 epoch 毫秒)。期貨日盤 08:45–13:45,證券 09:00–13:30。
export function sessionStart(symbol) {
  const d = new Date();
  if (symbol.kind === 'future') d.setHours(8, 45, 0, 0);
  else d.setHours(9, 0, 0, 0);
  return d.getTime();
}

export function sessionEnd(symbol) {
  const d = new Date();
  if (symbol.kind === 'future') d.setHours(13, 45, 0, 0);
  else d.setHours(13, 30, 0, 0);
  return d.getTime();
}
