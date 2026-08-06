using TwMarketWidget.Models;

namespace TwMarketWidget.ViewModels;

/// <summary>報價表格中的一列。物件本身重複使用，只換值，避免整張表閃爍。</summary>
public sealed class QuoteRowViewModel : ObservableObject
{
    /// <summary>走勢點上限。開盤起每 5 秒一點約 3200 點，留點餘裕。</summary>
    private const int MaxPoints = 6000;

    private readonly List<PricePoint> _series = new();

    private Quote? _quote;
    private bool _isStale;
    private IReadOnlyList<PricePoint> _seriesSnapshot = Array.Empty<PricePoint>();

    public QuoteRowViewModel(WatchSymbol symbol)
    {
        Symbol = symbol;
    }

    /// <summary>已經抓過當日開盤到現在的底線了，同一個交易日不再重抓。</summary>
    public DateTime? SeededOn { get; private set; }

    /// <summary>走勢線的 X 軸起點：當日開盤時間。期貨日盤 08:45 開，證券 09:00 開。</summary>
    public DateTime SessionStart => Symbol.Kind == SymbolKind.Future
        ? DateTime.Today.AddHours(8).AddMinutes(45)
        : DateTime.Today.AddHours(9);

    /// <summary>走勢線的 X 軸終點：當日收盤時間。期貨日盤 13:45 收，證券 13:30 收。</summary>
    public DateTime SessionEnd => Symbol.Kind == SymbolKind.Future
        ? DateTime.Today.AddHours(13).AddMinutes(45)
        : DateTime.Today.AddHours(13).AddMinutes(30);

    public WatchSymbol Symbol { get; }

    public string Key => Symbol.Key;

    public string Code => Symbol.Code;

    public string KindLabel => Symbol.Kind switch
    {
        SymbolKind.Stock => Symbol.Market.Equals("otc", StringComparison.OrdinalIgnoreCase) ? "上櫃" : "上市",
        SymbolKind.Index => "指數",
        SymbolKind.Future => "期貨",
        _ => "",
    };

    public string Name => Symbol.DisplayName ?? _quote?.Name ?? Symbol.Code;

    /// <summary>期貨實際對到的合約，例如 TXFH6；股票沒有這個概念。</summary>
    public string? Contract => _quote?.Contract;

    public string ToolTipText => Contract is null ? $"{Code} {Name}" : $"{Code} {Name}（{Contract}）";

    public decimal? Last => _quote?.Last;

    public decimal? Change => _quote?.Change;

    public decimal? ChangePercent => _quote?.ChangePercent;

    public decimal? Open => _quote?.Open;

    public decimal? High => _quote?.High;

    public decimal? Low => _quote?.Low;

    public decimal? PrevClose => _quote?.PrevClose;

    public decimal? Bid => _quote?.Bid;

    public decimal? Ask => _quote?.Ask;

    public long? Volume => _quote?.Volume;

    public DateTime? TradeTime => _quote?.TradeTime;

    public string TradeTimeText => _quote?.TradeTime?.ToString("HH:mm:ss") ?? "--:--:--";

    /// <summary>-1 跌、0 平、1 漲。UI 用它上色（台股習慣紅漲綠跌）。</summary>
    public int Direction => Change switch
    {
        null => 0,
        > 0 => 1,
        < 0 => -1,
        _ => 0,
    };

    /// <summary>這一輪沒抓到這檔的報價。</summary>
    public bool IsStale
    {
        get => _isStale;
        set => SetProperty(ref _isStale, value);
    }

    public bool HasData => _quote is not null;

    /// <summary>
    /// 當日走勢。每次變動都換成新的陣列，Sparkline 綁到這個屬性才會重畫
    /// （直接改同一個 List 的內容不會觸發重繪）。
    /// </summary>
    public IReadOnlyList<PricePoint> Series => _seriesSnapshot;

    public void Update(Quote quote)
    {
        _quote = quote;
        IsStale = false;

        if (quote.Last is { } price)
        {
            Append(new PricePoint(quote.TradeTime ?? DateTime.Now, price));
        }

        RaiseAll();
    }

    /// <summary>用開盤到現在的資料鋪底，接在既有的即時點之前。</summary>
    public void Seed(IReadOnlyList<PricePoint> history)
    {
        SeededOn = DateTime.Today;
        if (history.Count == 0)
        {
            return;
        }

        var live = _series.ToList();
        _series.Clear();
        _series.AddRange(history);
        // 即時點只保留比底線更新的，避免同一分鐘重複。
        var lastSeeded = history[^1].Time;
        _series.AddRange(live.Where(p => p.Time > lastSeeded));
        Trim();
        PublishSeries();
    }

    private void Append(PricePoint point)
    {
        // 同一秒重複回報就只更新最後一點，不要一直長。
        if (_series.Count > 0 && _series[^1].Time >= point.Time)
        {
            if (_series[^1].Price == point.Price)
            {
                return;
            }

            _series[^1] = point with { Time = _series[^1].Time };
        }
        else
        {
            _series.Add(point);
        }

        Trim();
        PublishSeries();
    }

    private void Trim()
    {
        if (_series.Count > MaxPoints)
        {
            _series.RemoveRange(0, _series.Count - MaxPoints);
        }
    }

    private void PublishSeries()
    {
        _seriesSnapshot = _series.ToArray();
        OnPropertyChanged(nameof(Series));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Contract));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(Last));
        OnPropertyChanged(nameof(Change));
        OnPropertyChanged(nameof(ChangePercent));
        OnPropertyChanged(nameof(Open));
        OnPropertyChanged(nameof(High));
        OnPropertyChanged(nameof(Low));
        OnPropertyChanged(nameof(PrevClose));
        OnPropertyChanged(nameof(Bid));
        OnPropertyChanged(nameof(Ask));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(TradeTime));
        OnPropertyChanged(nameof(TradeTimeText));
        OnPropertyChanged(nameof(Direction));
        OnPropertyChanged(nameof(HasData));
    }
}
