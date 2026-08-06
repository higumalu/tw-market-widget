# TwMarketWidget — 台股即時報價小工具

WPF (.NET 8) 桌面小工具，用公開來源顯示台灣股票、指數與期貨的即時報價。不需要券商帳號或 API 金鑰。

## 功能

- **精簡模式**：標題列按「精簡」收成一條窄長條，只留代號、名稱、當日走勢線、現價、漲跌幅；按「完整」切回大表。高度跟著檔數自動縮，寬度可自己拉
- **當日走勢線**：X 軸固定是當日「開盤～收盤」（證券 09:00–13:30、期貨日盤 08:45–13:45），
  線從左邊的開盤時間隨時間往右長，右邊留白就是還沒走完的盤；虛線是昨收／參考價，線的顏色跟著在平盤上或下
- 自選清單：上市／上櫃個股、指數、期貨混在同一張表
- 自動輪詢（預設 5 秒，可調 3～60 秒），可手動「立即更新」
- 紅漲綠跌配色，顯示成交、漲跌、幅度、五檔最佳買賣、開高低、昨收／參考價、總量、撮合時間
- 期貨自動對到近月標準月合約（滑鼠移到名稱可看實際合約，例如 `TXFH6`）
- **懸浮半透明**：無邊框視窗，透明度 15%～100% 可拉；只有底色變透明，數字永遠是實心的
- 預設釘在最上層，拖標題列可移動、雙擊可最大化、右下角可縮放；關掉時記住位置與大小
- 設定與清單存在 `%APPDATA%\TwMarketWidget\settings.json`
- 這一輪抓不到的商品會整列變淡，不會拿舊值假裝是新的

## 執行

```powershell
dotnet run --project TwMarketWidget.csproj
```

或直接跑編好的執行檔：

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\TwMarketWidget.exe
```

### 診斷模式

```powershell
.\bin\Debug\net8.0-windows\TwMarketWidget.exe --selftest
```

不開視窗，直接打一次 API，把結果寫到 `%TEMP%\TwMarketWidget-selftest.txt`。資料源掛掉或欄位改版時先用這個確認。

未預期的例外會寫到 `%TEMP%\TwMarketWidget-crash.txt`（無邊框視窗出事時不會只是無聲消失）。

## 發佈

推一個 `v` 開頭的 tag 就會跑 `.github/workflows/release.yml`，自動建置並開一個 GitHub Release：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

每次會產出兩包 win-x64 單一執行檔：

| 檔名 | 說明 |
| --- | --- |
| `TwMarketWidget-<版本>-win-x64-framework-dependent.zip` | 約 0.3 MB，需要先裝 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `TwMarketWidget-<版本>-win-x64-self-contained.zip` | 約 65 MB，內含執行環境，解壓縮就能跑 |

也可以在 Actions 頁面手動觸發（`workflow_dispatch`），只會產生 artifact、不會建 Release。
tag 上的版本號會寫進組件版本，例如 `v1.2.3` → `1.2.3`。

## 新增商品

在下方輸入代號、選市場別後按「加入」（或按 Enter）：

| 市場別 | 代號範例 | 說明 |
| --- | --- | --- |
| `tse` | `2330`、`2317` | 上市個股 |
| `otc` | `6488`、`5483` | 上櫃個股 |
| `index` | `t00`（加權）、`o00`（櫃買） | 指數 |
| `future` | `TX`、`MTX`、`TE`、`TF`、`TMF` | 期貨，取近月 |

期貨也可以直接填完整合約代號（例如 `TXFI6`）鎖定特定月份。

## 資料來源

| 來源 | 端點 | 用途 |
| --- | --- | --- |
| 證交所 MIS | `GET https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_2330.tw\|otc_6488.tw&json=1&delay=0` | 個股、指數即時報價 |
| 期交所 MIS | `POST https://mis.taifex.com.tw/futures/api/getQuoteList` | 期貨即時報價 |
| 期交所 MIS | `POST https://mis.taifex.com.tw/futures/api/getChartData1M` | 期貨當日一分鐘 K，走勢線的底 |
| Yahoo Finance | `GET https://query1.finance.yahoo.com/v8/finance/chart/2330.TW?interval=1m&range=1d` | 股票／指數走勢線的底 |

走勢線的畫法：先跟上面的分時 API 要一份「開盤到現在」鋪底，之後每次輪詢把即時價接在後面，所以線的尾巴是即時的。
證交所 MIS 原本的 `getChartInfo.jsp` 已經下架（現在回 404），股票那段底線才改用 Yahoo；Yahoo 的台股分時大約延遲 15～20 分鐘，
所以剛開程式時線的尾端到現在之間會有一小段直線，隨著輪詢累積就會補起來。抓不到底線的商品（例如櫃買指數）就從程式啟動開始畫。

實作上的幾個坑（都已經處理）：

- 證交所 `msgArray` 的 `z`（成交價）在盤前或當下無成交時是 `"-"`，程式會退回用最佳買價、再退回開盤價。
- 證交所回應帶 `userDelay: 5000`，也就是官方建議 5 秒輪詢一次；預設值就照這個設。輪太快有被擋的風險。
- 期交所的 request body 欄位必須是 **PascalCase**（`CID`、`RowSize`…）。用 `PostAsJsonAsync` 的預設 Web 慣例會被轉成 camelCase，伺服器認不得商品別，會一律回台指期的清單。
- 期交所 `QuoteList` 第一筆是現貨（`TXF-S`），後面才是期貨（`-F`），而且小台的清單裡混著週契約（`MX2H6`）。近月要取「代號以 CID 開頭」的第一筆才會是標準月契約。
- 期交所的 `CRefPrice` 是前一交易日結算價，漲跌以它為基準。

這兩個都是官網前端在用的公開端點，沒有 SLA，也可能改版；資料延遲以官方頁面為準，僅供參考，不要拿來當下單依據。

## 結構

```
Models/        WatchSymbol、Quote、PricePoint
Services/      TwseQuoteSource、TaifexQuoteSource、QuoteService（即時報價彙整）
               YahooIntradaySource、TaifexIntradaySource、IntradayService（走勢線）
               SettingsStore
ViewModels/    MainViewModel、QuoteRowViewModel、簡易 MVVM 基底
Controls/      Sparkline（自己畫的迷你走勢線，沒有圖表函式庫）
Converters/    漲跌上色、數字格式
Themes/Dark.xaml   深色樣式（含半透明視窗用的 ComboBox／按鈕樣板）
```

新增即時報價來源實作 `IQuoteSource`，新增走勢線來源實作 `IIntradaySource`，再塞進 `MainViewModel` 建構的
`QuoteService` / `IntradayService` 就好。
