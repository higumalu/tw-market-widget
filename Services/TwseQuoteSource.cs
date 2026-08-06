using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>
/// 證交所 MIS 即時報價（上市／上櫃個股與指數）。
/// 端點：https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_2330.tw|otc_6488.tw&amp;json=1&amp;delay=0
/// 必須先造訪 index.jsp 取得 session cookie，否則會被導回首頁。
/// </summary>
public sealed class TwseQuoteSource : IQuoteSource, IDisposable
{
    private const string IndexUrl = "https://mis.twse.com.tw/stock/index.jsp";
    private const string ApiUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/126.0.0.0 Safari/537.36";

    /// <summary>MIS 單次查詢的檔數上限，超過就分批。</summary>
    private const int BatchSize = 40;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private bool _sessionReady;

    public TwseQuoteSource()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", IndexUrl);
    }

    public bool CanHandle(WatchSymbol symbol) =>
        symbol.Kind is SymbolKind.Stock or SymbolKind.Index;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<WatchSymbol> symbols,
        CancellationToken cancellationToken)
    {
        var targets = symbols.Where(CanHandle).ToList();
        if (targets.Count == 0)
        {
            return Array.Empty<Quote>();
        }

        await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);

        var quotes = new List<Quote>(targets.Count);
        foreach (var batch in Chunk(targets, BatchSize))
        {
            quotes.AddRange(await FetchBatchAsync(batch, cancellationToken).ConfigureAwait(false));
        }

        return quotes;
    }

    private async Task<IReadOnlyList<Quote>> FetchBatchAsync(
        IReadOnlyList<WatchSymbol> batch,
        CancellationToken cancellationToken)
    {
        var exCh = string.Join('|', batch.Select(s => s.ExCh));
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{ApiUrl}?ex_ch={Uri.EscapeDataString(exCh)}&json=1&delay=0&_={stamp}";

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (doc.RootElement.TryGetProperty("rtcode", out var rtcode) &&
            rtcode.GetString() is { } code && code != "0000")
        {
            // session 過期時 MIS 會回非 0000，下一輪重新取 cookie。
            _sessionReady = false;
            var message = doc.RootElement.TryGetProperty("rtmessage", out var m) ? m.GetString() : null;
            throw new HttpRequestException($"證交所回應錯誤 rtcode={code} {message}");
        }

        if (!doc.RootElement.TryGetProperty("msgArray", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Quote>();
        }

        var byExCh = batch.ToDictionary(s => s.ExCh, StringComparer.OrdinalIgnoreCase);
        var quotes = new List<Quote>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            var itemCode = Str(item, "c");
            var exchange = Str(item, "ex");
            if (itemCode is null || exchange is null)
            {
                continue;
            }

            if (!byExCh.TryGetValue($"{exchange}_{itemCode}.tw", out var symbol))
            {
                continue;
            }

            quotes.Add(ToQuote(item, symbol));
        }

        return quotes;
    }

    private static Quote ToQuote(JsonElement item, WatchSymbol symbol)
    {
        var bid = FirstPrice(Str(item, "b"));
        var ask = FirstPrice(Str(item, "a"));
        // 無成交時 z 會是 "-"，用委買價遞補，仍沒有就退回開盤價。
        var last = Num(item, "z") ?? bid ?? ask ?? Num(item, "o");

        return new Quote
        {
            Key = symbol.Key,
            Code = symbol.Code,
            Name = symbol.DisplayName ?? Str(item, "n") ?? Str(item, "nf"),
            Last = last,
            Open = Num(item, "o"),
            High = Num(item, "h"),
            Low = Num(item, "l"),
            PrevClose = Num(item, "y"),
            Bid = bid,
            Ask = ask,
            Volume = (long?)Num(item, "v"),
            TradeTime = TradeTime(item),
        };
    }

    private static DateTime? TradeTime(JsonElement item)
    {
        if (Str(item, "tlong") is { } tlong &&
            long.TryParse(tlong, out var epochMs) && epochMs > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToLocalTime().DateTime;
        }

        if (Str(item, "t") is { } t && TimeSpan.TryParse(t, out var time))
        {
            return DateTime.Today.Add(time);
        }

        return null;
    }

    /// <summary>五檔欄位是 "580.0000_579.0000_..." 這種格式，取第一檔。</summary>
    private static decimal? FirstPrice(string? packed)
    {
        if (string.IsNullOrWhiteSpace(packed))
        {
            return null;
        }

        var first = packed.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Parse(first);
    }

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? Num(JsonElement item, string name) => Parse(Str(item, name));

    private static decimal? Parse(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionReady)
        {
            return;
        }

        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionReady)
            {
                return;
            }

            try
            {
                // 只是為了拿 session cookie；index.jsp 偶爾回 404，但報價 API 照樣能用，
                // 所以這裡失敗不影響後續查詢。
                using var response = await _http.GetAsync(IndexUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
            }

            _sessionReady = true;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _sessionLock.Dispose();
    }
}
