using System.Net;
using System.Net.Http;
using System.Text.Json;
using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>
/// 股票與指數的當日分時線。證交所 MIS 原本的 getChartInfo.jsp 已經下架（現在回 404），
/// 所以這條「從開盤到現在」的底線改用 Yahoo 的一分鐘資料，即時價仍然來自證交所。
/// 抓不到就回空的，走勢線改成從程式啟動開始畫。
/// </summary>
public sealed class YahooIntradaySource : IIntradaySource, IDisposable
{
    private const string ChartUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";

    private readonly HttpClient _http;

    public YahooIntradaySource()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
    }

    public bool CanHandle(WatchSymbol symbol) =>
        symbol.Kind is SymbolKind.Stock or SymbolKind.Index && ToYahooSymbol(symbol) is not null;

    public async Task<IReadOnlyList<PricePoint>> GetIntradayAsync(
        WatchSymbol symbol,
        string? contract,
        CancellationToken cancellationToken)
    {
        if (ToYahooSymbol(symbol) is not { } yahooSymbol)
        {
            return Array.Empty<PricePoint>();
        }

        var url = $"{ChartUrl}{Uri.EscapeDataString(yahooSymbol)}?interval=1m&range=1d";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return Array.Empty<PricePoint>();
        }

        var result = results[0];
        if (!result.TryGetProperty("timestamp", out var timestamps) ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array ||
            quotes.GetArrayLength() == 0 ||
            !quotes[0].TryGetProperty("close", out var closes))
        {
            return Array.Empty<PricePoint>();
        }

        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        var points = new List<PricePoint>(count);

        for (var i = 0; i < count; i++)
        {
            if (closes[i].ValueKind != JsonValueKind.Number)
            {
                continue; // 沒成交的分鐘 Yahoo 會給 null
            }

            var time = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).ToLocalTime().DateTime;
            points.Add(new PricePoint(time, closes[i].GetDecimal()));
        }

        return points;
    }

    private static string? ToYahooSymbol(WatchSymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Stock => symbol.Market.Equals("otc", StringComparison.OrdinalIgnoreCase)
            ? $"{symbol.Code}.TWO"
            : $"{symbol.Code}.TW",
        SymbolKind.Index => symbol.Code.ToLowerInvariant() switch
        {
            "t00" => "^TWII",
            "o00" => "^TWOII",
            _ => null,
        },
        _ => null,
    };

    public void Dispose() => _http.Dispose();
}
