# 架構

.NET 8 WPF，**零 NuGet 依賴**（MVVM 基底、走勢線繪製都是手刻的），所有第三方互動都在 `Services/` 底下。

```
Models/        WatchSymbol（自選項目）、Quote（報價快照）、PricePoint（走勢點）
Services/      IQuoteSource   → TwseQuoteSource、TaifexQuoteSource      → QuoteService
               IIntradaySource → YahooIntradaySource、TaifexIntradaySource → IntradayService
               SettingsStore（%APPDATA% 設定檔）
ViewModels/    MainViewModel、QuoteRowViewModel、ObservableObject、RelayCommand
Controls/      Sparkline（自繪走勢線）
Converters/    漲跌上色、數字格式
Themes/        Dark.xaml（含半透明視窗專用的控制項樣板）
```

## 資料流

```
DispatcherTimer (預設 5s)
  └─ MainViewModel.RefreshAsync()
       ├─ QuoteService.GetQuotesAsync(自選清單)
       │    ├─ TwseQuoteSource   ← 個股、指數（每批 40 檔）
       │    └─ TaifexQuoteSource ← 期貨（同一 CID 只打一次，多檔共用結果）
       │         → 依 Quote.Key 對回每一列，更新值並把成交價接到走勢線尾巴
       └─ SeedPendingSeriesAsync()   ← 只在該列還沒有當日底線時跑
            └─ IntradayService.GetIntradayAsync()
                 ├─ YahooIntradaySource   ← 個股、指數
                 └─ TaifexIntradaySource  ← 期貨（要先知道近月是哪一口）
```

新增報價來源實作 `IQuoteSource`、新增走勢線來源實作 `IIntradaySource`，塞進 `MainViewModel` 建構的
`QuoteService` / `IntradayService` 即可，其他地方不用動。

## 幾個刻意的設計

### 列物件重複使用

`QuoteRowViewModel` 一列一個物件，每輪只換值不重建，避免整張表閃爍與捲動位置跳掉。
這一輪沒回報的商品標成 `IsStale`（整列變淡），**不會拿舊值假裝是新的**。

### 走勢線的資料只換整包，不就地改

`Sparkline.Series` 是 `DependencyProperty` 且帶 `AffectsRender`。直接改同一個 `List` 的內容不會觸發重繪，
所以 `QuoteRowViewModel` 每次變動都把 `_series` 複製成一個新陣列再發 `PropertyChanged`（`PublishSeries()`）。
點數上限 6000（開盤起每 5 秒一點約 3200 點），超過就從頭砍。

### 底線 + 即時尾巴

走勢線分兩段來源：`Seed()` 放官方分時資料當底，之後每輪 `Append()` 把即時價接在後面。
`Seed()` 會把比底線更新的即時點保留下來接回去，同一秒重複回報只更新最後一點，不會一直長。
期貨要等第一次報價回來、知道近月合約是哪一口（`Quote.Contract`）才抓得動底線，所以補底線放在每次更新之後檢查，
用 `SeededOn` 記交易日、`_seeding` 集合擋重入。

### X 軸釘在盤中時段

`Sparkline` 給了 `SessionStart` / `SessionEnd` 就照時間比例算 X，線從開盤往右長，右邊留白是還沒走完的盤；
沒給就退回「所有點平均攤在寬度上」。時段落在 `QuoteRowViewModel`：

| 商品 | 開盤 | 收盤 |
| --- | --- | --- |
| 證券（個股、指數） | 09:00 | 13:30 |
| 期貨（日盤） | 08:45 | 13:45 |

時段外的點（例如期貨夜盤）不畫。Y 軸範圍會把參考價一起算進去，線才看得出來在平盤上或下。

### 半透明只吃底色

用 `Border.Background` 綁一個 `SolidColorBrush` 的 `Opacity`，**不是** `Window.Opacity`——
後者會連文字一起變淡，報價就看不清楚了。

```xml
<Border.Background>
    <SolidColorBrush Color="{StaticResource BackgroundColor}" Opacity="{Binding BackgroundOpacity}" />
</Border.Background>
```

### 無邊框視窗要自己補回來的東西

`WindowStyle="None"` + `AllowsTransparency="True"` 之後，系統標題列沒了，所以：

- 標題列自己畫，`MouseLeftButtonDown` 呼叫 `DragMove()` 拖曳、雙擊切換最大化
- 最小化 / 關閉自己接
- `ResizeMode="CanResizeWithGrip"` 保留縮放
- **系統預設的 `ComboBox` 樣板在這種視窗上會變成白底白字**，`Themes/Dark.xaml` 裡自己寫了一份 `ControlTemplate`
- 出事的話視窗只會無聲消失，所以 `App` 掛了 `DispatcherUnhandledException`，例外寫到 `%TEMP%\TwMarketWidget-crash.txt`

### 精簡 / 完整模式

同一個視窗切版面，不是兩個視窗，polling 迴圈維持一份。
精簡模式把工具列、大表、編輯列、狀態列收起來，只留 `ItemsControl` 的窄列；
視窗改成 `SizeToContent="Height"`（高度跟著檔數縮）、`MinWidth` 一起換掉，完整模式的尺寸先記在
`MainWindow._fullWidth/_fullHeight`，切回去才回得原本大小。

### 設定

`%APPDATA%\TwMarketWidget\settings.json`：自選清單、輪詢秒數、透明度、釘選、視窗位置與大小、精簡模式與其寬度。
**設定檔壞掉不該讓程式開不起來**，`SettingsStore.Load()` 讀失敗就回預設值；存檔失敗也只是安靜略過，不打斷報價。
