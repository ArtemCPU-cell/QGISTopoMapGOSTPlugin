namespace OsmToShapefile.Scale;

/// <summary>
/// Поддерживаемые масштабы. Цифры — знаменатель (10k → 1:10 000).
/// </summary>
public enum MapScale
{
    Scale10k = 10000,
    Scale25k = 25000,
    Scale50k = 50000,
    Scale100k = 100000,
    Scale200k = 200000,
}