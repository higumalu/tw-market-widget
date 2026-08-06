using System.IO;
using System.Windows;
using TwMarketWidget.Services;

namespace TwMarketWidget;

public partial class App : Application
{
    /// <summary>啟動或執行中炸掉時把例外寫出來，不然無邊框視窗只會無聲消失。</summary>
    private static readonly string CrashLogPath =
        Path.Combine(Path.GetTempPath(), "TwMarketWidget-crash.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            File.WriteAllText(CrashLogPath, $"{DateTime.Now:s}\n{args.Exception}");
            MessageBox.Show(args.Exception.Message, "台股即時報價 發生錯誤");
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            File.WriteAllText(CrashLogPath, $"{DateTime.Now:s}\n{args.ExceptionObject}");

        // 診斷用：TwMarketWidget.exe --selftest 會直接打一次 API，把結果寫到
        // %TEMP%\TwMarketWidget-selftest.txt，不開視窗。用來確認資料源是否還活著。
        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            // 跑在背景執行緒上：UI 執行緒的 SynchronizationContext 會讓同步等待死結。
            Task.Run(RunSelfTest).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static async Task RunSelfTest()
    {
        var path = Path.Combine(Path.GetTempPath(), "TwMarketWidget-selftest.txt");
        var lines = new List<string> { $"self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}" };

        var settings = new SettingsStore().Load();
        using var service = new QuoteService(new TwseQuoteSource(), new TaifexQuoteSource());

        try
        {
            var result = await service.GetQuotesAsync(settings.Watchlist, CancellationToken.None);
            foreach (var quote in result.Quotes)
            {
                lines.Add($"{quote.Code,-6} {quote.Contract,-7} {quote.Name,-14} last={quote.Last} " +
                          $"chg={quote.Change} open={quote.Open} high={quote.High} low={quote.Low} " +
                          $"prev={quote.PrevClose} bid={quote.Bid} ask={quote.Ask} " +
                          $"vol={quote.Volume} t={quote.TradeTime:HH:mm:ss}");
            }

            lines.Add($"取得 {result.Quotes.Count}/{settings.Watchlist.Count} 檔");

            using var intraday = new IntradayService(new YahooIntradaySource(), new TaifexIntradaySource());
            foreach (var symbol in settings.Watchlist)
            {
                var contract = result.Quotes.FirstOrDefault(q => q.Key == symbol.Key)?.Contract;
                var series = await intraday.GetIntradayAsync(symbol, contract, CancellationToken.None);
                lines.Add(series.Count == 0
                    ? $"分時 {symbol.Code,-6} 無資料"
                    : $"分時 {symbol.Code,-6} {series.Count,4} 點 " +
                      $"{series[0].Time:HH:mm}~{series[^1].Time:HH:mm} " +
                      $"首 {series[0].Price} 末 {series[^1].Price}");
            }
            lines.AddRange(result.Errors.Select(err => $"error: {err}"));
        }
        catch (Exception ex)
        {
            lines.Add($"fatal: {ex}");
        }

        await File.WriteAllLinesAsync(path, lines);
    }
}
