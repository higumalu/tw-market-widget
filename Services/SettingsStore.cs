using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwMarketWidget.Models;

namespace TwMarketWidget.Services;

public sealed class AppSettings
{
    public int RefreshSeconds { get; set; } = 5;

    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>背景不透明度 0.15～1。只影響底色，文字永遠是實心的。</summary>
    public double BackgroundOpacity { get; set; } = 0.85;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 520;

    /// <summary>精簡模式：只有代號、名稱、走勢秒線、現價。</summary>
    public bool CompactMode { get; set; }

    public double CompactWidth { get; set; } = 420;

    public List<WatchSymbol> Watchlist { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
        Watchlist = new List<WatchSymbol>
        {
            new() { Code = "t00", Kind = SymbolKind.Index, Market = "tse", DisplayName = "加權指數" },
            new() { Code = "o00", Kind = SymbolKind.Index, Market = "otc", DisplayName = "櫃買指數" },
            new() { Code = "TX", Kind = SymbolKind.Future, DisplayName = "台指期近月" },
            new() { Code = "MTX", Kind = SymbolKind.Future, DisplayName = "小型台指近月" },
            new() { Code = "2330", Kind = SymbolKind.Stock, Market = "tse" },
            new() { Code = "2317", Kind = SymbolKind.Stock, Market = "tse" },
            new() { Code = "2454", Kind = SymbolKind.Stock, Market = "tse" },
        },
    };
}

/// <summary>把設定與自選清單存到 %APPDATA%\TwMarketWidget\settings.json。</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TwMarketWidget",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppSettings.CreateDefault();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
            if (settings is null || settings.Watchlist.Count == 0)
            {
                return AppSettings.CreateDefault();
            }

            settings.RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 3, 60);
            settings.BackgroundOpacity = Math.Clamp(settings.BackgroundOpacity, 0.15, 1.0);
            settings.WindowWidth = Math.Clamp(settings.WindowWidth, 600, 4000);
            settings.WindowHeight = Math.Clamp(settings.WindowHeight, 200, 3000);
            settings.CompactWidth = Math.Clamp(settings.CompactWidth, 320, 900);
            return settings;
        }
        catch (Exception)
        {
            // 設定檔壞掉不該讓程式開不起來。
            return AppSettings.CreateDefault();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception)
        {
            // 存檔失敗就算了，不打斷報價。
        }
    }
}
