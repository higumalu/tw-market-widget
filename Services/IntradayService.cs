using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

/// <summary>把分時走勢的請求分派給對應來源。</summary>
public sealed class IntradayService : IDisposable
{
    private readonly IReadOnlyList<IIntradaySource> _sources;

    public IntradayService(params IIntradaySource[] sources)
    {
        _sources = sources;
    }

    public async Task<IReadOnlyList<PricePoint>> GetIntradayAsync(
        WatchSymbol symbol,
        string? contract,
        CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.CanHandle(symbol));
        if (source is null)
        {
            return Array.Empty<PricePoint>();
        }

        try
        {
            return await source.GetIntradayAsync(symbol, contract, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 底線抓不到不影響即時報價，走勢就從現在開始畫。
            return Array.Empty<PricePoint>();
        }
    }

    public void Dispose()
    {
        foreach (var source in _sources.OfType<IDisposable>())
        {
            source.Dispose();
        }
    }
}
