// 當日走勢迷你折線,對應 C# 版 Controls\Sparkline.cs 的 OnRender。
// 參考價(昨收／前一日結算)畫成虛線;線的顏色跟著最後一點在參考價之上或之下。

const COLORS = {
  up: '#F45151',
  down: '#3FC17A',
  flat: '#8A93A2',
};

// canvas: <canvas>;series: [{t, price}];baseline: 參考價或 null;
// sessionStart/End: epoch 毫秒或 null。
export function drawSparkline(canvas, series, baseline, sStart, sEnd) {
  const ctx = canvas.getContext('2d');
  const dpr = window.devicePixelRatio || 1;
  const cssW = canvas.clientWidth;
  const cssH = canvas.clientHeight;
  if (cssW <= 2 || cssH <= 2) return;

  // 依 CSS 尺寸配 backing store,畫面才不糊。
  const bw = Math.round(cssW * dpr);
  const bh = Math.round(cssH * dpr);
  if (canvas.width !== bw || canvas.height !== bh) {
    canvas.width = bw;
    canvas.height = bh;
  }
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, cssW, cssH);

  if (!series || series.length < 2) return;

  // 有給盤中時段就把 X 軸釘在「開盤～收盤」,線只畫到目前為止,右邊留白;
  // 沒給就退回:所有點平均攤在寬度上。
  const timeScaled = sStart != null && sEnd != null && sEnd > sStart;
  const pts = timeScaled
    ? series.filter((p) => p.t >= sStart && p.t <= sEnd)
    : series;
  if (pts.length < 2) return;

  let min = Infinity;
  let max = -Infinity;
  for (const p of pts) {
    if (p.price < min) min = p.price;
    if (p.price > max) max = p.price;
  }

  const hasBase = baseline !== null && baseline !== undefined && Number.isFinite(Number(baseline));
  const base = hasBase ? Number(baseline) : NaN;
  if (hasBase) {
    // 把參考價一起納入範圍,線才看得出來在平盤上或下。
    min = Math.min(min, base);
    max = Math.max(max, base);
  }

  let range = max - min;
  if (range <= 0) {
    range = Math.max(Math.abs(max) * 0.001, 0.01);
    min -= range / 2;
  }

  const pad = 2;
  const plotH = Math.max(cssH - pad * 2, 1);
  const Y = (v) => pad + ((max - v) / range) * plotH;

  const last = pts[pts.length - 1].price;
  let color = COLORS.flat;
  if (hasBase && Math.abs(last - base) > 1e-9) color = last > base ? COLORS.up : COLORS.down;

  // 參考價虛線
  if (hasBase) {
    ctx.save();
    ctx.strokeStyle = COLORS.flat;
    ctx.lineWidth = 1;
    ctx.setLineDash([3, 3]);
    const y = Y(base);
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(cssW, y);
    ctx.stroke();
    ctx.restore();
  }

  const span = timeScaled ? sEnd - sStart : 0;
  const step = pts.length > 1 ? cssW / (pts.length - 1) : cssW;
  const X = (i) => (timeScaled ? ((pts[i].t - sStart) / span) * cssW : i * step);

  ctx.strokeStyle = color;
  ctx.lineWidth = 1.4;
  ctx.lineJoin = 'round';
  ctx.beginPath();
  ctx.moveTo(X(0), Y(pts[0].price));
  for (let i = 1; i < pts.length; i++) ctx.lineTo(X(i), Y(pts[i].price));
  ctx.stroke();
}
