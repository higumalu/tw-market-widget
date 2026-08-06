using System.Collections.ObjectModel;
using System.Windows.Threading;
using TwMarketWidget.Models;
using TwMarketWidget.Services;

namespace TwMarketWidget.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly QuoteService _quotes;
    private readonly IntradayService _intraday;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _seeding = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _inflight;

    private string _statusText = "尚未更新";
    private string? _errorText;
    private bool _isBusy;
    private QuoteRowViewModel? _selectedRow;
    private string _newCode = "";
    private string _newMarket = "tse";

    public MainViewModel()
    {
        _store = new SettingsStore();
        _settings = _store.Load();
        _quotes = new QuoteService(new TwseQuoteSource(), new TaifexQuoteSource());
        _intraday = new IntradayService(new YahooIntradaySource(), new TaifexIntradaySource());

        foreach (var symbol in _settings.Watchlist)
        {
            Rows.Add(new QuoteRowViewModel(symbol));
        }

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy);
        ToggleModeCommand = new RelayCommand(() => CompactMode = !CompactMode);
        AddCommand = new RelayCommand(AddSymbol, () => !string.IsNullOrWhiteSpace(NewCode));
        RemoveCommand = new RelayCommand(RemoveSelected, () => SelectedRow is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedRow is not null);
        MoveDownCommand = new RelayCommand(() => Move(1), () => SelectedRow is not null);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RefreshSeconds) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public ObservableCollection<QuoteRowViewModel> Rows { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ToggleModeCommand { get; }

    public RelayCommand AddCommand { get; }

    public RelayCommand RemoveCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public IReadOnlyList<string> MarketOptions { get; } = new[] { "tse", "otc", "index", "future" };

    public int RefreshSeconds
    {
        get => _settings.RefreshSeconds;
        set
        {
            var clamped = Math.Clamp(value, 3, 60);
            if (_settings.RefreshSeconds == clamped)
            {
                return;
            }

            _settings.RefreshSeconds = clamped;
            if (_timer is not null)
            {
                _timer.Interval = TimeSpan.FromSeconds(clamped);
            }

            OnPropertyChanged();
            Save();
        }
    }

    public bool AlwaysOnTop
    {
        get => _settings.AlwaysOnTop;
        set
        {
            if (_settings.AlwaysOnTop == value)
            {
                return;
            }

            _settings.AlwaysOnTop = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>背景不透明度。只套在底色的筆刷上，數字文字不會跟著變淡。</summary>
    public double BackgroundOpacity
    {
        get => _settings.BackgroundOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0.15, 1.0);
            if (Math.Abs(_settings.BackgroundOpacity - clamped) < 0.001)
            {
                return;
            }

            _settings.BackgroundOpacity = clamped;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>精簡模式：收起工具列與大表，只留代號、名稱、秒線、現價。</summary>
    public bool CompactMode
    {
        get => _settings.CompactMode;
        set
        {
            if (_settings.CompactMode == value)
            {
                return;
            }

            _settings.CompactMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FullMode));
            OnPropertyChanged(nameof(ModeButtonText));
            Save();
        }
    }

    public bool FullMode => !CompactMode;

    public string ModeButtonText => CompactMode ? "完整" : "精簡";

    public double CompactWidth
    {
        get => _settings.CompactWidth;
        set => _settings.CompactWidth = Math.Clamp(value, 320, 900);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public QuoteRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>新增輸入框：代號（2330 / TX / t00）。</summary>
    public string NewCode
    {
        get => _newCode;
        set => SetProperty(ref _newCode, value);
    }

    /// <summary>新增輸入框：市場別，見 <see cref="MarketOptions"/>。</summary>
    public string NewMarket
    {
        get => _newMarket;
        set => SetProperty(ref _newMarket, value);
    }

    public double? WindowLeft => _settings.WindowLeft;

    public double? WindowTop => _settings.WindowTop;

    public double WindowWidth => _settings.WindowWidth;

    public double WindowHeight => _settings.WindowHeight;

    /// <summary>關閉前記住視窗位置與大小，下次開在同一個地方。</summary>
    public void UpdateWindowBounds(double left, double top, double width, double height)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var symbols = Rows.Select(r => r.Symbol).ToList();
        if (symbols.Count == 0)
        {
            StatusText = "自選清單是空的";
            return;
        }

        IsBusy = true;
        _inflight?.Cancel();
        _inflight?.Dispose();
        _inflight = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var result = await _quotes.GetQuotesAsync(symbols, _inflight.Token).ConfigureAwait(true);
            var byKey = result.Quotes.ToDictionary(q => q.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var row in Rows)
            {
                if (byKey.TryGetValue(row.Key, out var quote))
                {
                    row.Update(quote);
                }
                else
                {
                    row.IsStale = true;
                }
            }

            StatusText = $"更新於 {DateTime.Now:HH:mm:ss}（{byKey.Count}/{Rows.Count} 檔）";
            ErrorText = result.Errors.Count > 0 ? string.Join("；", result.Errors.Distinct()) : null;

            _ = SeedPendingSeriesAsync();
        }
        catch (OperationCanceledException)
        {
            ErrorText = "查詢逾時";
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 幫還沒有底線的商品補上「當日開盤到現在」的走勢。期貨要等第一次報價回來、
    /// 知道近月是哪一口之後才抓得動，所以放在每次更新之後檢查。
    /// </summary>
    private async Task SeedPendingSeriesAsync()
    {
        foreach (var row in Rows.ToList())
        {
            if (row.SeededOn == DateTime.Today || !row.HasData)
            {
                continue;
            }

            if (row.Symbol.Kind == SymbolKind.Future && row.Contract is null)
            {
                continue;
            }

            if (!_seeding.Add(row.Key))
            {
                continue;
            }

            try
            {
                var history = await _intraday
                    .GetIntradayAsync(row.Symbol, row.Contract, CancellationToken.None)
                    .ConfigureAwait(true);
                row.Seed(history);
            }
            finally
            {
                _seeding.Remove(row.Key);
            }
        }
    }

    private void AddSymbol()
    {
        var code = NewCode.Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            return;
        }

        var symbol = NewMarket switch
        {
            "future" => new WatchSymbol { Code = code, Kind = SymbolKind.Future },
            "index" => new WatchSymbol
            {
                Code = code.ToLowerInvariant(),
                Kind = SymbolKind.Index,
                Market = code.StartsWith("O", StringComparison.OrdinalIgnoreCase) ? "otc" : "tse",
            },
            var market => new WatchSymbol { Code = code, Kind = SymbolKind.Stock, Market = market },
        };

        if (Rows.Any(r => r.Key.Equals(symbol.Key, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorText = $"{code} 已在清單中";
            return;
        }

        Rows.Add(new QuoteRowViewModel(symbol));
        NewCode = "";
        Save();
        _ = RefreshAsync();
    }

    private void RemoveSelected()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        Rows.Remove(row);
        SelectedRow = null;
        Save();
    }

    private void Move(int offset)
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
        SelectedRow = row;
        Save();
    }

    private void Save()
    {
        _settings.Watchlist = Rows.Select(r => r.Symbol).ToList();
        _store.Save(_settings);
    }

    public void Dispose()
    {
        _timer.Stop();
        _inflight?.Cancel();
        _inflight?.Dispose();
        _quotes.Dispose();
        _intraday.Dispose();
        Save();
    }
}
