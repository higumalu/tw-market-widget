using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>
/// 期交所 MIS 即時報價。以商品別（CID）查一次 getQuoteList，
/// 取近月（最早到期月份）那一口作為「TX 近月」之類的報價。
/// </summary>
public sealed class TaifexQuoteSource : IQuoteSource, IDisposable
{
    private const string QuoteListUrl = "https://mis.taifex.com.tw/futures/api/getQuoteList";
    private const string Referer = "https://mis.taifex.com.tw/futures/RegularSession/EquityIndices/FuturesDomestic/";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/126.0.0.0 Safari/537.36";

    /// <summary>使用者輸入的代號 → 期交所商品代碼（CID）。</summary>
    private static readonly Dictionary<string, string> CidAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TX"] = "TXF",   // 臺股期貨
        ["MTX"] = "MXF",  // 小型臺指期貨
        ["TMF"] = "TMF",  // 微型臺指期貨
        ["TE"] = "EXF",   // 電子期貨
        ["TF"] = "FXF",   // 金融期貨
        ["T5F"] = "T5F",  // 臺灣50期貨
    };

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly HttpClient _http;

    public TaifexQuoteSource()
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
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://mis.taifex.com.tw");
    }

    public bool CanHandle(WatchSymbol symbol) => symbol.Kind == SymbolKind.Future;

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<WatchSymbol> symbols,
        CancellationToken cancellationToken)
    {
        var targets = symbols.Where(CanHandle).ToList();
        if (targets.Count == 0)
        {
            return Array.Empty<Quote>();
        }

        var quotes = new List<Quote>(targets.Count);
        // 同一個 CID 只查一次，多檔共用結果。
        foreach (var group in targets.GroupBy(ResolveCid, StringComparer.OrdinalIgnoreCase))
        {
            var rows = await FetchProductAsync(group.Key, cancellationToken).ConfigureAwait(false);
            foreach (var symbol in group)
            {
                if (PickContract(rows, group.Key, symbol.Code) is { } row)
                {
                    quotes.Add(ToQuote(row, symbol));
                }
            }
        }

        return quotes;
    }

    private static string ResolveCid(WatchSymbol symbol) =>
        CidAliases.TryGetValue(symbol.Code, out var cid) ? cid : symbol.Code.ToUpperInvariant();

    private async Task<IReadOnlyList<JsonElement>> FetchProductAsync(
        string cid,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            MarketType = "0",
            SymbolType = "F",
            KindID = "1",
            CID = cid,
            ExpireMonth = "",
            RowSize = "100",
            PageNo = "",
            SortColumn = "",
            SortOrder = "",
        };

        // 必須維持 PascalCase 欄位名。PostAsJsonAsync 預設走 Web 慣例會轉成 camelCase，
        // 期交所認不得 cID 就會忽略商品別，一律回台指期的清單。
        using var content = JsonContent.Create(payload, options: PayloadOptions);
        using var response = await _http.PostAsync(QuoteListUrl, content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = doc.RootElement;
        if (root.TryGetProperty("RtCode", out var rtCode) &&
            rtCode.ToString() is { } code && code is not ("0" or "0000"))
        {
            var message = root.TryGetProperty("RtMsg", out var m) ? m.GetString() : null;
            throw new HttpRequestException($"期交所回應錯誤 RtCode={code} {message}");
        }

        if (!root.TryGetProperty("RtData", out var data) ||
            !data.TryGetProperty("QuoteList", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        // JsonDocument 會在離開 using 後回收，先 Clone 起來。
        return list.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    /// <summary>
    /// 選出要顯示的合約。QuoteList 第一筆是現貨（SymbolID 以 -S 結尾），
    /// 之後是由近而遠的各月份期貨（-F），中間可能夾雜週契約（小台的 MX2H6 等）。
    /// 使用者若直接輸入完整合約代號（例如 TXFI6）就用那一口，
    /// 否則取「代號以 CID 開頭」的第一口，也就是標準月契約的近月。
    /// </summary>
    private static JsonElement? PickContract(IReadOnlyList<JsonElement> rows, string cid, string code)
    {
        JsonElement? monthly = null;
        JsonElement? anyFuture = null;

        foreach (var row in rows)
        {
            var symbolId = Str(row, "SymbolID") ?? "";
            if (symbolId.Contains('/'))
            {
                continue; // 跨月價差組合單
            }

            var contract = symbolId.Split('-')[0];
            if (contract.Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }

            if (!symbolId.EndsWith("-F", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            anyFuture ??= row;
            if (monthly is null && contract.StartsWith(cid, StringComparison.OrdinalIgnoreCase))
            {
                monthly = row;
            }
        }

        return monthly ?? anyFuture;
    }

    private static Quote ToQuote(JsonElement row, WatchSymbol symbol)
    {
        var bid = Num(row, "CBidPrice1") ?? Num(row, "CBestBidPrice");
        var ask = Num(row, "CAskPrice1") ?? Num(row, "CBestAskPrice");

        // CRefPrice 是前一交易日結算價，期交所的漲跌 CDiff 就是以它為基準。
        var prevClose = Num(row, "CRefPrice") ?? Num(row, "SettlementPrice");
        var last = Num(row, "CLastPrice") ?? bid ?? ask;

        if (prevClose is null && last is { } l && Num(row, "CDiff") is { } diff)
        {
            prevClose = l - diff;
        }

        return new Quote
        {
            Key = symbol.Key,
            Code = symbol.Code,
            Name = symbol.DisplayName ?? Str(row, "DispCName") ?? Str(row, "SymbolID"),
            Contract = Str(row, "SymbolID")?.Split('-')[0],
            Last = last,
            Open = Num(row, "COpenPrice"),
            High = Num(row, "CHighPrice"),
            Low = Num(row, "CLowPrice"),
            PrevClose = prevClose,
            Bid = bid,
            Ask = ask,
            Volume = (long?)Num(row, "CTotalVolume"),
            TradeTime = TradeTime(row),
        };
    }

    private static DateTime? TradeTime(JsonElement row)
    {
        var time = Str(row, "CTime");
        if (string.IsNullOrWhiteSpace(time))
        {
            return null;
        }

        // 期交所回 HHmmss（有時帶毫秒）。
        var digits = new string(time.Where(char.IsDigit).ToArray());
        if (digits.Length >= 6 &&
            int.TryParse(digits[..2], out var h) &&
            int.TryParse(digits.Substring(2, 2), out var m) &&
            int.TryParse(digits.Substring(4, 2), out var s) &&
            h < 24 && m < 60 && s < 60)
        {
            return DateTime.Today.AddHours(h).AddMinutes(m).AddSeconds(s);
        }

        return TimeSpan.TryParse(time, out var parsed) ? DateTime.Today.Add(parsed) : null;
    }

    private static string? Str(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null,
            }
            : null;

    private static decimal? Num(JsonElement row, string name)
    {
        var text = Str(row, name);
        return !string.IsNullOrWhiteSpace(text) &&
               decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public void Dispose() => _http.Dispose();
}
