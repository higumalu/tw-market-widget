'use strict';

const path = require('path');
const fs = require('fs');
const os = require('os');
const { app, BrowserWindow, ipcMain } = require('electron');

const settingsStore = require('./settings');
const feed = require('./sources');

let win = null;
let settings = settingsStore.createDefault();
let currentCompact = false; // 供關閉時決定怎麼記錄視窗尺寸

// 出事時視窗常常無聲消失,把例外寫到 %TEMP%,對應 C# 的 crash txt。
function logCrash(prefix, err) {
  try {
    const line = `[${new Date().toISOString()}] ${prefix}: ${err && err.stack ? err.stack : err}\n`;
    fs.appendFileSync(path.join(os.tmpdir(), 'TwMarketWidget-crash.txt'), line, 'utf8');
  } catch {
    /* 記錄失敗也不能再拋 */
  }
}

process.on('uncaughtException', (err) => logCrash('uncaughtException', err));
process.on('unhandledRejection', (err) => logCrash('unhandledRejection', err));

function createWindow() {
  settings = settingsStore.load();
  currentCompact = !!settings.compactMode;

  const opts = {
    width: settings.windowWidth,
    height: settings.windowHeight,
    minWidth: 280,
    minHeight: 120,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    resizable: true,
    skipTaskbar: false,
    show: false,
    title: '台股即時報價',
    webPreferences: {
      preload: path.join(__dirname, '..', 'preload', 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      spellcheck: false,
    },
  };
  if (settings.windowLeft !== null && settings.windowTop !== null) {
    opts.x = Math.round(settings.windowLeft);
    opts.y = Math.round(settings.windowTop);
  }

  // 工作列/視窗圖示。打包後 exe 自帶圖示,這裡主要讓 dev(npm start)也有。
  const iconPath = path.join(__dirname, '..', '..', 'build', 'icon.ico');
  if (fs.existsSync(iconPath)) opts.icon = iconPath;

  win = new BrowserWindow(opts);
  win.setAlwaysOnTop(!!settings.alwaysOnTop, 'screen-saver');
  win.removeMenu();
  win.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));

  win.once('ready-to-show', () => win.show());

  // 關閉前記住視窗位置與大小(對應 MainWindow.SaveBounds)。
  win.on('close', () => {
    if (!win) return;
    const b = win.getBounds();
    settings.windowLeft = b.x;
    settings.windowTop = b.y;
    if (currentCompact) {
      // 精簡模式:寬度記成 compactWidth,完整尺寸維持不變。
      settings.compactWidth = Math.round(b.width);
    } else {
      settings.windowWidth = Math.round(b.width);
      settings.windowHeight = Math.round(b.height);
    }
    settingsStore.save(settings);
  });

  win.on('closed', () => {
    win = null;
  });
}

// ── IPC:報價與走勢(在 main 抓,沒有 CORS 限制) ──────────────
ipcMain.handle('quotes:fetch', async (_e, symbols) => {
  const ac = new AbortController();
  const timer = setTimeout(() => ac.abort(), 20000); // 對應 C# 的 20 秒逾時
  try {
    return await feed.getQuotes(symbols, ac.signal);
  } catch (err) {
    if (err && err.name === 'AbortError') return { quotes: [], errors: ['查詢逾時'] };
    return { quotes: [], errors: [err && err.message ? err.message : String(err)] };
  } finally {
    clearTimeout(timer);
  }
});

ipcMain.handle('intraday:fetch', async (_e, { symbol, contract }) => {
  const ac = new AbortController();
  const timer = setTimeout(() => ac.abort(), 20000);
  try {
    return await feed.getIntraday(symbol, contract, ac.signal);
  } catch {
    return [];
  } finally {
    clearTimeout(timer);
  }
});

// ── IPC:設定 ────────────────────────────────────────────
ipcMain.handle('settings:load', () => settings);

ipcMain.handle('settings:save', (_e, next) => {
  // 保留視窗尺寸/位置(那些由 main 在 close 時寫入),其餘採用 renderer 的值。
  const preserved = {
    windowLeft: settings.windowLeft,
    windowTop: settings.windowTop,
    windowWidth: settings.windowWidth,
    windowHeight: settings.windowHeight,
  };
  settings = { ...settings, ...next, ...preserved };
  currentCompact = !!settings.compactMode;
  settingsStore.save(settings);
  return true;
});

// ── IPC:視窗控制 ─────────────────────────────────────────
ipcMain.on('win:minimize', () => win && win.minimize());
ipcMain.on('win:close', () => win && win.close());
ipcMain.on('win:toggle-maximize', () => {
  if (!win) return;
  win.isMaximized() ? win.unmaximize() : win.maximize();
});
ipcMain.on('win:set-always-on-top', (_e, value) => {
  if (win) win.setAlwaysOnTop(!!value, 'screen-saver');
});
// 精簡模式高度跟著列數縮(對應 WPF 的 SizeToContent=Height)。
ipcMain.on('win:resize', (_e, { width, height }) => {
  if (!win || win.isMaximized()) return;
  const b = win.getBounds();
  win.setBounds({
    x: b.x,
    y: b.y,
    width: Math.max(280, Math.round(width || b.width)),
    height: Math.max(80, Math.round(height || b.height)),
  });
});
ipcMain.on('ui:mode', (_e, compact) => {
  currentCompact = !!compact;
});

// ── 生命週期 ─────────────────────────────────────────────
app.whenReady().then(createWindow);

app.on('window-all-closed', () => app.quit());
app.on('activate', () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow();
});
