using System.Text.Json.Serialization;

namespace TwMarketWidget.Models;

public enum SymbolKind
{
    /// <summary>上市／上櫃個股。</summary>
    Stock,

    /// <summary>指數（加權、櫃買、電子類等）。</summary>
    Index,

    /// <summary>期交所期貨商品。</summary>
    Future,
}

/// <summary>自選清單中的一檔商品。</summary>
public sealed class WatchSymbol
{
    /// <summary>商品代號：個股 2330、指數 t00、期貨 TX。</summary>
    public string Code { get; set; } = "";

    public SymbolKind Kind { get; set; } = SymbolKind.Stock;

    /// <summary>證交所來源專用：tse（上市）或 otc（上櫃）。期貨忽略此欄。</summary>
    public string Market { get; set; } = "tse";

    /// <summary>使用者自訂顯示名稱；空的話用 API 回傳的名稱。</summary>
    public string? DisplayName { get; set; }

    /// <summary>證交所 MIS 的 ex_ch 參數，例如 tse_2330.tw。</summary>
    [JsonIgnore]
    public string ExCh => $"{Market}_{Code}.tw";

    /// <summary>整份清單中唯一識別一檔商品的鍵。</summary>
    [JsonIgnore]
    public string Key => Kind == SymbolKind.Future ? $"F:{Code}" : $"{Market}:{Code}";

    public WatchSymbol Clone() => new()
    {
        Code = Code,
        Kind = Kind,
        Market = Market,
        DisplayName = DisplayName,
    };
}
