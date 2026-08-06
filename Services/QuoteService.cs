using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

public sealed record QuoteResult(IReadOnlyList<Quote> Quotes, IReadOnlyList<string> Errors);

/// <summary>把自選清單分派給各報價來源，再把結果合起來。</summary>
public sealed class QuoteService : IDisposable
{
    private readonly IReadOnlyList<IQuoteSource> _sources;

    public QuoteService(params IQuoteSource[] sources)
    {
        _sources = sources;
    }

    public async Task<QuoteResult> GetQuotesAsync(
        IReadOnlyList<WatchSymbol> symbols,
        CancellationToken cancellationToken)
    {
        var tasks = _sources
            .Select(source => new
            {
                Source = source,
                Symbols = (IReadOnlyList<WatchSymbol>)symbols.Where(source.CanHandle).ToList(),
            })
            .Where(x => x.Symbols.Count > 0)
            .Select(async x =>
            {
                try
                {
                    return (Quotes: await x.Source.GetQuotesAsync(x.Symbols, cancellationToken)
                        .ConfigureAwait(false), Error: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return (Quotes: (IReadOnlyList<Quote>)Array.Empty<Quote>(), Error: ex.Message);
                }
            })
            .ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return new QuoteResult(
            results.SelectMany(r => r.Quotes).ToList(),
            results.Select(r => r.Error).OfType<string>().ToList());
    }

    public void Dispose()
    {
        foreach (var source in _sources.OfType<IDisposable>())
        {
            source.Dispose();
        }
    }
}
