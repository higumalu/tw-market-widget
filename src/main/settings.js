'use strict';

// 設定與自選清單存到 app.getPath('userData')/settings.json。
// 對應 C# 版 Services\SettingsStore.cs。設定檔壞掉不該讓程式開不起來 —— 讀失敗回預設值。

const fs = require('fs');
const path = require('path');
const { app } = require('electron');

const clamp = (v, lo, hi) => Math.min(Math.max(v, lo), hi);

function createDefault() {
  return {
    refreshSeconds: 5,
    alwaysOnTop: true,
    // 背景不透明度 0.15～1。只影響底色,文字永遠是實心的。
    backgroundOpacity: 0.85,
    windowLeft: null,
    windowTop: null,
    windowWidth: 1240,
    windowHeight: 520,
    compactMode: false,
    compactWidth: 420,
    watchlist: [
      { code: 't00', kind: 'index', market: 'tse', displayName: '加權指數' },
      { code: 'o00', kind: 'index', market: 'otc', displayName: '櫃買指數' },
      { code: 'TX', kind: 'future', market: 'tse', displayName: '台指期近月' },
      { code: 'MTX', kind: 'future', market: 'tse', displayName: '小型台指近月' },
      { code: '2330', kind: 'stock', market: 'tse', displayName: null },
      { code: '2317', kind: 'stock', market: 'tse', displayName: null },
      { code: '2454', kind: 'stock', market: 'tse', displayName: null },
    ],
  };
}

function filePath() {
  return path.join(app.getPath('userData'), 'settings.json');
}

function load() {
  try {
    const p = filePath();
    if (!fs.existsSync(p)) return createDefault();

    const parsed = JSON.parse(fs.readFileSync(p, 'utf8'));
    if (!parsed || !Array.isArray(parsed.watchlist) || parsed.watchlist.length === 0) {
      return createDefault();
    }

    const def = createDefault();
    const s = { ...def, ...parsed };
    s.refreshSeconds = clamp(Number(s.refreshSeconds) || 5, 3, 60);
    s.backgroundOpacity = clamp(Number(s.backgroundOpacity) || 0.85, 0.15, 1.0);
    s.windowWidth = clamp(Number(s.windowWidth) || def.windowWidth, 600, 4000);
    s.windowHeight = clamp(Number(s.windowHeight) || def.windowHeight, 200, 3000);
    s.compactWidth = clamp(Number(s.compactWidth) || def.compactWidth, 320, 900);
    return s;
  } catch {
    // 設定檔壞掉不該讓程式開不起來。
    return createDefault();
  }
}

function save(settings) {
  try {
    const p = filePath();
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, JSON.stringify(settings, null, 2), 'utf8');
  } catch {
    // 存檔失敗就算了,不打斷報價。
  }
}

module.exports = { load, save, createDefault, filePath };
