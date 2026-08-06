# 資料來源與 API 契約

四個端點，都是官網前端在用的公開 API，沒有 SLA、沒有金鑰、也可能無預警改版。
本文記的欄位與範例都是實際打出來驗過的（2026-08 盤中）。

| 用途 | 端點 | 對應程式 |
| --- | --- | --- |
| 個股／指數即時報價 | `GET mis.twse.com.tw/stock/api/getStockInfo.jsp` | `Services/TwseQuoteSource.cs` |
| 期貨即時報價 | `POST mis.taifex.com.tw/futures/api/getQuoteList` | `Services/TaifexQuoteSource.cs` |
| 期貨當日一分鐘 K | `POST mis.taifex.com.tw/futures/api/getChartData1M` | `Services/TaifexIntradaySource.cs` |
| 個股／指數當日分時 | `GET query1.finance.yahoo.com/v8/finance/chart/…` | `Services/YahooIntradaySource.cs` |

---

## 1. 證交所 MIS：個股與指數即時報價

```bash
curl 'https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_2330.tw%7Ctse_t00.tw%7Cotc_o00.tw&json=1&delay=0&_=1785980565000' \
  -H 'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) …' \
  -H 'Referer: https://mis.twse.com.tw/stock/index.jsp' \
  -H 'Accept: application/json, text/plain, */*'
```

### `ex_ch` 寫法

多檔用 `|` 串起來（要 URL encode 成 `%7C`）。

| 類型 | 寫法 | 範例 |
| --- | --- | --- |
| 上市個股 | `tse_<代號>.tw` | `tse_2330.tw` |
| 上櫃個股 | `otc_<代號>.tw` | `otc_6488.tw` |
| 加權指數 | `tse_t00.tw` | 發行量加權股價指數 |
| 櫃買指數 | `otc_o00.tw` | 櫃買指數 |

### 回應

```json
{
  "msgArray": [{
    "c": "2330", "n": "台積電", "nf": "台灣積體電路製造股份有限公司", "ex": "tse",
    "z": "-", "o": "2395.0000", "h": "2395.0000", "l": "2365.0000", "y": "2405.0000",
    "a": "2370.0000_2375.0000_2380.0000_2385.0000_2390.0000_",
    "b": "2365.0000_2360.0000_2355.0000_2350.0000_2345.0000_",
    "f": "157_183_236_379_381_", "g": "263_720_759_997_334_",
    "v": "7559", "tv": "-", "u": "2645.0000", "w": "2165.0000",
    "t": "09:42:45", "tlong": "1785980565000", "d": "20260806"
  }],
  "userDelay": 5000, "rtcode": "0000", "rtmessage": "OK"
}
```

| 欄位 | 意義 | 備註 |
| --- | --- | --- |
| `c` / `n` / `nf` | 代號 / 簡稱 / 全名 | |
| `ex` | `tse` 或 `otc` | 用 `ex`+`c` 組回 `ex_ch` 來對回自選清單 |
| `z` | 成交價 | **無成交時是 `"-"`** |
| `o` `h` `l` | 開 / 高 / 低 | |
| `y` | 昨收 | 漲跌以此為基準 |
| `a` / `b` | 五檔賣價 / 買價 | 底線分隔，取第一段是最佳一檔 |
| `f` / `g` | 五檔賣量 / 買量 | 同上格式 |
| `v` / `tv` | 累計成交量（張）/ 單筆成交量 | 指數沒有這兩欄 |
| `u` / `w` | 漲停 / 跌停價 | |
| `t` / `tlong` / `d` | 撮合時間 / epoch 毫秒 / 日期 | 程式優先用 `tlong` |
| `rtcode` | `0000` 才是正常 | 非 0000 時程式會丟掉 session 重取 |
| `userDelay` | 官方建議輪詢間隔（毫秒） | 實測回 `5000`，預設值就照這個設 |

### 坑

- **`z` 會是 `"-"`**：盤前或當下沒有成交就沒有成交價。程式的退路是「最佳買價 → 最佳賣價 → 開盤價」。
- **session cookie**：原本要先 GET `https://mis.twse.com.tw/stock/index.jsp` 取 cookie。該頁現在回 404，但報價 API 照樣能用，
  所以 `EnsureSessionAsync` 吞掉這一步的失敗，不讓它擋住查詢。
- **一次的檔數**：程式切成每批 40 檔。批太大容易吃到伺服器端限制。
- **輪詢頻率**：跟著 `userDelay` 走（5 秒）。打太快有被擋的風險。

---

## 2. 期交所 MIS：期貨即時報價

```bash
curl -X POST 'https://mis.taifex.com.tw/futures/api/getQuoteList' \
  -H 'Content-Type: application/json' \
  -H 'Referer: https://mis.taifex.com.tw/futures/RegularSession/EquityIndices/FuturesDomestic/' \
  -H 'Origin: https://mis.taifex.com.tw' \
  -d '{"MarketType":"0","SymbolType":"F","KindID":"1","CID":"TXF","ExpireMonth":"","RowSize":"100","PageNo":"","SortColumn":"","SortOrder":""}'
```

### 商品代碼（CID）

程式把使用者輸入的代號轉成 CID：

| 輸入 | CID | 商品 |
| --- | --- | --- |
| `TX` | `TXF` | 臺股期貨（大台） |
| `MTX` | `MXF` | 小型臺指期貨 |
| `TMF` | `TMF` | 微型臺指期貨 |
| `TE` | `EXF` | 電子期貨 |
| `TF` | `FXF` | 金融期貨 |
| `T5F` | `T5F` | 臺灣 50 期貨 |

沒對到的就直接把輸入當 CID 送。CID 無效時伺服器回 `RtCode: "2"`、`RtMsg: "查無資料"`。

### 回應

```json
{"RtCode":"0","RtMsg":"","RtData":{"QuoteCount":"8","QuoteList":[
  {"SymbolID":"MXF-S","DispCName":"小臺指現貨","CLastPrice":"44149.73","CRefPrice":"44611.60", …},
  {"SymbolID":"MX2H6-F","DispCName":"小臺指期W2086","CLastPrice":"43914.00","CTotalVolume":"12", …},
  {"SymbolID":"MXFH6-F","DispCName":"小臺指期086","CLastPrice":"43992.00","CTotalVolume":"56423",
   "COpenPrice":"44142.00","CHighPrice":"44330.00","CLowPrice":"43847.00","CRefPrice":"44537.00",
   "CBidPrice1":"43992.00","CAskPrice1":"43996.00","CDiff":"-545.00","CDiffRate":"-1.22",
   "CDate":"20260806","CTime":"095908"}
]}}
```

| 欄位 | 意義 |
| --- | --- |
| `SymbolID` | `TXF-S` 是現貨、`TXFH6-F` 是期貨、含 `/` 的是跨月價差組合單 |
| `DispCName` / `DispEName` | 中文 / 英文名稱，例如 `臺指期086` / `TX086` |
| `CLastPrice` | 成交價 |
| `CRefPrice` | 前一交易日結算價，**漲跌 `CDiff` 以它為基準** |
| `COpenPrice` `CHighPrice` `CLowPrice` | 開 / 高 / 低 |
| `CTotalVolume` | 當日累計成交量（口） |
| `CBidPrice1`～`5` / `CAskPrice1`～`5` | 五檔買賣價，另有 `CBidSize*` / `CAskSize*` |
| `CBestBidPrice` / `CBestAskPrice` | 最佳買賣價（現貨列是空字串） |
| `CDiff` / `CDiffRate` / `CAmpRate` | 漲跌 / 漲跌幅 / 振幅 |
| `CCeilPrice` / `CFloorPrice` | 漲停 / 跌停 |
| `CDate` / `CTime` | `yyyyMMdd` / `HHmmss` |
| `CTestPrice` / `CTestTime` | 試撮價與時間 |

### 月份代碼

`SymbolID` 的月份用 `A`～`L` 對應 1～12 月，後面接年份個位數。
`TXFH6` = `TXF` + `H`(第 8 個字母 → 8 月) + `6`(2026) = 2026 年 8 月，與 `DispCName` 的 `臺指期086` 一致。

### 坑

- **body 欄位必須是 PascalCase**。`HttpClient.PostAsJsonAsync` 預設走 Web 慣例，會把 `CID` 序列化成 `cID`，
  伺服器認不得就忽略商品別，**一律回台指期的清單** —— 表面上有資料、值卻是錯的（小台顯示大台的價量）。
  程式改用 `JsonContent.Create(payload, new JsonSerializerOptions { PropertyNamingPolicy = null })`。
- **第一列是現貨**（`-S` 結尾），不是期貨。
- **清單裡混著週契約**：小台的近月不是第一筆 `-F`（那是週契約 `MX2H6`），
  要取「代號以 CID 開頭」的第一筆才是標準月契約 `MXFH6`。
- `RowSize` 送 `"全部"` 也可以，但只要送出端不是 UTF-8 就會 400（用 curl 在 Big5 終端機測會踩到）。程式送 `"100"` 避開。

---

## 3. 期交所 MIS：期貨當日一分鐘 K

```bash
curl -X POST 'https://mis.taifex.com.tw/futures/api/getChartData1M' \
  -H 'Content-Type: application/json' \
  -H 'Referer: https://mis.taifex.com.tw/futures/RegularSession/EquityIndices/FuturesDomestic/' \
  -d '{"SymbolID":"TXFH6-F"}'
```

```json
{"RtCode":"0","RtData":{
  "SymbolID":"TXFH6-F","DispCName":"臺指期086",
  "Info":{"Status":"0","Sessions":[{"Start":"0845","End":"1345"}]},
  "Quote":{"COpenPrice":"44177.00", …},
  "Ticks":[["084500","44177.00","44180.00","44170.00","44175.00","523"],
           ["103000","44103.00","44118.00","44082.00","44112.00","30"]]
}}
```

- `Ticks` 每筆是 `[時間 HHmmss, 開, 高, 低, 收, 量]`，程式取**收盤價**當走勢點。
- `Info.Sessions` 就是該商品的盤中時段（日盤 08:45–13:45）。程式目前是寫死時段，沒有讀這欄。
- **`SymbolID` 要傳字串**，傳陣列會 400（`Cannot deserialize instance of java.lang.String out of START_ARRAY token`）。
- 資料是即時的，實測 10:38 就拿得到 08:46～10:38 共 113 點。

---

## 4. Yahoo Finance：個股與指數當日分時

```bash
curl 'https://query1.finance.yahoo.com/v8/finance/chart/2330.TW?interval=1m&range=1d'
```

| 商品 | Yahoo 代號 |
| --- | --- |
| 上市個股 | `2330.TW` |
| 上櫃個股 | `6488.TWO` |
| 加權指數 | `^TWII` |
| 櫃買指數 | `^TWOII` |

取 `chart.result[0].timestamp[]`（epoch 秒）搭配 `chart.result[0].indicators.quote[0].close[]`。

### 坑

- **為什麼不用證交所的**：MIS 原本的分時圖 API `getChartInfo.jsp` 已經下架，現在回 404（連帶試過 `getDailyChart.jsp` 也是）。
  找不到官方替代品，才用 Yahoo 補這段。
- **延遲 15～20 分鐘**：實測 10:37 抓到的資料只到 10:16。所以它只當「底線」，線的尾巴由證交所的即時輪詢接上，
  中間那段空缺會隨著程式跑一陣子自己補起來。
- **沒成交的分鐘 `close` 會是 `null`**，要跳過。
- **`^TWOII` 沒有分時資料**（實測回空）。抓不到底線的商品就從程式啟動開始畫。

---

## 共通注意事項

- 全部都是官網前端在用的端點，沒有版本承諾，欄位隨時可能改。改版時先跑 `--selftest` 對照本文的欄位表。
- 資料延遲以官方頁面為準，這個工具僅供參考，不要當下單依據。
- 任何一個來源掛掉都不會讓程式當掉：報價來源的例外會收進狀態列的錯誤訊息，走勢線來源失敗就回空清單。
