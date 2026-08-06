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

## 下載

到 [Releases](https://github.com/higumalu/tw-market-widget/releases) 抓 win-x64 單一執行檔：

| 檔名 | 說明 |
| --- | --- |
| `…-framework-dependent.zip` | 約 0.3 MB，需要先裝 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `…-self-contained.zip` | 約 65 MB，內含執行環境，解壓縮就能跑 |

## 從原始碼執行

```powershell
dotnet run --project TwMarketWidget.csproj
```

沒有任何 NuGet 依賴，有 .NET 8 SDK（含 Windows Desktop）就能 build。
診斷、驗證與發佈流程見 [docs/development.md](docs/development.md)。

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

走勢線的畫法：先跟分時 API 要一份「開盤到現在」鋪底，之後每次輪詢把即時價接在後面，所以線的尾巴是即時的。
Yahoo 的台股分時大約延遲 15～20 分鐘，所以剛開程式時線的尾端到現在之間會有一小段直線，隨著輪詢累積就會補起來；
抓不到底線的商品（例如櫃買指數）就從程式啟動開始畫。

這些都是官網前端在用的公開端點，沒有 SLA，也可能改版；資料延遲以官方頁面為準，僅供參考，不要拿來當下單依據。
完整的欄位對照表、curl 範例與踩過的坑寫在 [docs/data-sources.md](docs/data-sources.md)。

## 文件

| 文件 | 內容 |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | 專案結構、資料流、走勢線與半透明視窗的實作決策 |
| [docs/data-sources.md](docs/data-sources.md) | 四個 API 的完整契約、欄位對照表、實測範例與坑 |
| [docs/development.md](docs/development.md) | 環境、診斷模式、UI 驗證方式、發佈流程與打包細節 |
