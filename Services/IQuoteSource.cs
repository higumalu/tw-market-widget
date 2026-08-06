using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>一個報價來源（證交所、期交所…）。</summary>
public interface IQuoteSource
{
    bool CanHandle(WatchSymbol symbol);

    /// <summary>抓一批報價。傳回的清單允許少於輸入（查不到的就不回）。</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlyList<WatchSymbol> symbols,
        CancellationToken cancellationToken);
}
