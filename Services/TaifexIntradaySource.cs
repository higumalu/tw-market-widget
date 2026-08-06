using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>
/// 期交所當日一分鐘 K：POST https://mis.taifex.com.tw/futures/api/getChartData1M
/// body 是 {"SymbolID":"TXFH6-F"}（字串，不是陣列），
/// 回傳 RtData.Ticks = [["HHmmss", 開, 高, 低, 收, 量], ...]，取收盤價當走勢點。
/// </summary>
public sealed class TaifexIntradaySource : IIntradaySource, IDisposable
{
    private const string ChartUrl = "https://mis.taifex.com.tw/futures/api/getChartData1M";
    private const string Referer = "https://mis.taifex.com.tw/futures/RegularSession/EquityIndices/FuturesDomestic/";

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = null };

    private readonly HttpClient _http;

    public TaifexIntradaySource()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/126.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://mis.taifex.com.tw");
    }

    public bool CanHandle(WatchSymbol symbol) => symbol.Kind == SymbolKind.Future;

    public async Task<IReadOnlyList<PricePoint>> GetIntradayAsync(
        WatchSymbol symbol,
        string? contract,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contract))
        {
            return Array.Empty<PricePoint>();
        }

        using var content = JsonContent.Create(new { SymbolID = $"{contract}-F" }, options: PayloadOptions);
        using var response = await _http.PostAsync(ChartUrl, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("RtData", out var data) ||
            !data.TryGetProperty("Ticks", out var ticks) ||
            ticks.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PricePoint>();
        }

        var today = DateTime.Today;
        var points = new List<PricePoint>(ticks.GetArrayLength());

        foreach (var tick in ticks.EnumerateArray())
        {
            if (tick.ValueKind != JsonValueKind.Array || tick.GetArrayLength() < 5)
            {
                continue;
            }

            var time = ParseTime(tick[0].GetString(), today);
            var close = ParseDecimal(tick[4].GetString());
            if (time is { } t && close is { } price)
            {
                points.Add(new PricePoint(t, price));
            }
        }

        return points;
    }

    private static DateTime? ParseTime(string? hhmmss, DateTime day)
    {
        if (hhmmss is null || hhmmss.Length < 6)
        {
            return null;
        }

        if (!int.TryParse(hhmmss[..2], out var h) ||
            !int.TryParse(hhmmss.Substring(2, 2), out var m) ||
            !int.TryParse(hhmmss.Substring(4, 2), out var s) ||
            h > 23 || m > 59 || s > 59)
        {
            return null;
        }

        var time = day.AddHours(h).AddMinutes(m).AddSeconds(s);
        // 夜盤從 15:00 開到隔天凌晨，凌晨的點屬於前一天的交易日。
        return time;
    }

    private static decimal? ParseDecimal(string? text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    public void Dispose() => _http.Dispose();
}
