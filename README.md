# tw-market-electron — 台股即時報價小工具（Electron 版）

用公開來源顯示台灣股票、指數與期貨的即時報價的懸浮桌面小工具。
這是 [`tw-market-widget`](../tw-market-widget)（.NET 8 WPF）的 **Electron 重寫版**，
功能一致，換技術棧的主要目的是**縮小被防毒／EDR 誤判攔截的面積**。

## 功能（對齊 WPF 版）

- **精簡／完整雙模式**：右上角「精簡」收成窄長條，只留代號、名稱、當日走勢秒線、現價、漲跌幅；精簡模式高度跟著檔數自動縮。
- **當日走勢線**：X 軸固定「開盤～收盤」（證券 09:00–13:30、期貨日盤 08:45–13:45），線從開盤往右長，右邊留白是還沒走完的盤；虛線是昨收／參考價，顏色跟著平盤上下。
- 自選清單：上市／上櫃個股、指數、期貨混在同一張表；可新增、刪除、上移／下移。
- 自動輪詢（預設 5 秒，可調 3～60 秒）與手動「立即更新」。
- 紅漲綠跌；顯示成交、漲跌、幅度、最佳買賣、開高低、昨收／參考、總量、撮合時間。
- 期貨自動對到近月標準月合約（滑鼠移到名稱看實際合約，例如 `TXFH6`）。
- **懸浮半透明**：無邊框、可調透明度（只有底色變透明，數字永遠實心）、預設釘在最上層、拖標題列移動、雙擊最大化、記住視窗位置與大小。
- 這一輪抓不到的商品整列變淡，不拿舊值假裝是新的。
- 設定與清單存在 `%APPDATA%\tw-market-electron\settings.json`。

## 架構

```
src/
  main/                 主行程（Node，負責抓資料 → 沒有瀏覽器 CORS 限制）
    index.js            視窗建立、IPC、透明/置頂、視窗尺寸持久化、崩潰紀錄
    settings.js         %APPDATA% 設定檔（對應 WPF 的 SettingsStore）
    sources/
      twse.js           證交所 MIS 個股/指數即時報價
      taifex.js         期交所 MIS 期貨即時報價 + 當日一分鐘 K
      yahoo.js          Yahoo Finance 個股/指數分時（走勢線底線）
      util.js           共用解析（num / firstPrice / 時間）
      index.js          聚合層（對應 QuoteService / IntradayService）
  preload/preload.js    唯一橋樑：contextBridge 暴露白名單 API
  renderer/             畫面（Chromium）
    index.html styles.css
    app.js              Row 模型、輪詢、底線補齊、雙模式、設定（對應 MainViewModel）
    sparkline.js        Canvas 走勢線（對應 Controls\Sparkline.cs）
    symbols.js format.js 代號/場別邏輯、數值格式化
```

資料流與 WPF 版一致：`main` 抓即時報價 → 對回每一列、把成交價接到走勢線尾巴 → 幫還沒有底線的列補「開盤到現在」的分時鋪底。**所有網路請求都在 main 行程**，因此不受瀏覽器同源政策限制，能照舊帶自訂 `User-Agent`/`Referer`/`Cookie`、送 PascalCase 的期交所 body。

安全性：`contextIsolation` + `sandbox` + `nodeIntegration:false`，renderer 只能透過 `window.api` 的白名單方法與 main 溝通；HTML 掛了嚴格 CSP。

## 開發與執行

需要 [Node.js](https://nodejs.org/)（18+，建議 20）。

```powershell
npm install
npm start
```

## 打包

```powershell
npm run pack:dir     # 免安裝資料夾 release\win-unpacked\（最推薦）
npm run pack:nsis    # 標準安裝器 release\TwMarketWidget-<版本>-setup.exe
npm run dist         # 兩種都產生
```

## 關於「避免被防毒擋掉」

換技術棧只能縮小誤判面積，**不是萬靈丹**。攔截通常有兩層原因，要分開處理：

**A. 跟技術棧無關（換 stack 也不會好，但影響最大）**

1. **MOTW（Mark of the Web）**：從網路下載的檔案帶「下載來源」標記，行為監控型 EDR（Trend Micro Apex One、CrowdStrike 等）會特別盯。**最有效的規避**是不觸發 MOTW：讓使用者本機 `git clone` 後自己 build、或走 winget／公司內部已信任的軟體派送管道。
2. **沒有程式碼簽章**：未簽章 exe 是 SmartScreen／防毒告警的頭號原因。有憑證時在 `electron-builder.yml` 的 `win.signtoolOptions` 填入即可（檔案裡已留好註解 hook）。**這是最有效的一招。**

**B. 跟技術棧有關（這版特意處理）**

3. **不用單一自解壓 exe**：`electron-builder` 的 `portable` target 會產生開機時把整包解壓到 `%TEMP%` 再執行的 stub —— 這正是 packer/dropper 的行為特徵，啟發式引擎最愛擋。本專案只用 `dir`（免安裝資料夾，零執行期解壓）與 `nsis`（標準安裝器）。
4. **完整中繼資料**：保留 `productName`／`appId`／版本／發行者，資訊齊全比空白中繼資料可疑度低。

**建議的散布順序**：本機 build（完全不碰 MOTW）＞ 已簽章的 NSIS 安裝器 ＞ 免安裝資料夾 zip。體積比 WPF 版大（內含 Chromium，約 100MB+），這是選 Electron 的代價。

## 資料來源

| 來源 | 端點 | 用途 |
| --- | --- | --- |
| 證交所 MIS | `GET mis.twse.com.tw/stock/api/getStockInfo.jsp` | 個股、指數即時報價 |
| 期交所 MIS | `POST mis.taifex.com.tw/futures/api/getQuoteList` | 期貨即時報價 |
| 期交所 MIS | `POST mis.taifex.com.tw/futures/api/getChartData1M` | 期貨當日一分鐘 K |
| Yahoo Finance | `GET query1.finance.yahoo.com/v8/finance/chart/…` | 股票／指數走勢線底線 |

都是官網前端在用的公開端點，沒有 SLA、也可能改版；資料延遲以官方頁面為準，僅供參考，不要當下單依據。
完整欄位對照與踩過的坑，見 WPF 版的 [`docs/data-sources.md`](../tw-market-widget/docs/data-sources.md)（契約相同）。
