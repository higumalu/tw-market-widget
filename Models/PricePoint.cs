namespace TwMarketWidget.Models;

/// <summary>分時走勢上的一個點。</summary>
public readonly record struct PricePoint(DateTime Time, decimal Price);
