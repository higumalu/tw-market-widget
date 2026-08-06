'use strict';

// 唯一的 renderer ↔ main 橋樑。contextIsolation + sandbox 下,renderer 只能透過
// window.api 呼叫這些白名單方法,拿不到 Node / ipcRenderer 本體。

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  // 資料
  fetchQuotes: (symbols) => ipcRenderer.invoke('quotes:fetch', symbols),
  fetchIntraday: (symbol, contract) =>
    ipcRenderer.invoke('intraday:fetch', { symbol, contract }),

  // 設定
  loadSettings: () => ipcRenderer.invoke('settings:load'),
  saveSettings: (settings) => ipcRenderer.invoke('settings:save', settings),

  // 視窗控制
  minimize: () => ipcRenderer.send('win:minimize'),
  close: () => ipcRenderer.send('win:close'),
  toggleMaximize: () => ipcRenderer.send('win:toggle-maximize'),
  setAlwaysOnTop: (value) => ipcRenderer.send('win:set-always-on-top', value),
  resizeTo: (width, height) => ipcRenderer.send('win:resize', { width, height }),
  setMode: (compact) => ipcRenderer.send('ui:mode', compact),
});
