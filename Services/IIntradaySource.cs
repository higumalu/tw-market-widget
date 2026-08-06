using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>
/// 當日分時走勢的起始資料。程式啟動時先跟這裡要一份「從開盤到現在」的線，
/// 之後才由輪詢把即時價一點一點接在後面。
/// </summary>
public interface IIntradaySource
{
    bool CanHandle(WatchSymbol symbol);

    /// <param name="contract">期貨已解析出來的合約代號（例如 TXFH6），其他商品傳 null。</param>
    Task<IReadOnlyList<PricePoint>> GetIntradayAsync(
        WatchSymbol symbol,
        string? contract,
        CancellationToken cancellationToken);
}
