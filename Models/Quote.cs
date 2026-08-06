namespace TwMarketWidget.Models;

/// <summary>單一商品的一筆報價快照。</summary>
public sealed class Quote
{
    public required string Key { get; init; }

    public required string Code { get; init; }

    public string? Name { get; init; }

    /// <summary>來源端的實際商品代號，例如期貨的近月合約 TXFH6。</summary>
    public string? Contract { get; init; }

    /// <summary>成交價。盤前或當日無成交時為 null。</summary>
    public decimal? Last { get; init; }

    public decimal? Open { get; init; }

    public decimal? High { get; init; }

    public decimal? Low { get; init; }

    /// <summary>參考價（個股為昨收，期貨為前一交易日結算價）。</summary>
    public decimal? PrevClose { get; init; }

    public decimal? Bid { get; init; }

    public decimal? Ask { get; init; }

    /// <summary>當日累計成交量（股票為張、期貨為口）。</summary>
    public long? Volume { get; init; }

    /// <summary>來源回報的撮合時間。</summary>
    public DateTime? TradeTime { get; init; }

    public decimal? Change =>
        Last is { } last && PrevClose is { } prev ? last - prev : null;

    public decimal? ChangePercent =>
        Last is { } last && PrevClose is { } prev && prev != 0
            ? (last - prev) / prev * 100m
            : null;
}
