namespace OsmToShapefile.Style;

public static class GostPalette
{
    // Гидрография
    public const string WaterStroke = "#3E78B2";     // линия реки
    public const string WaterFill   = "#9FC8E8";     // полигон озера
    public const string WetlandFill = "#B6D6C4";     // болото — салатовый

    // Растительность
    public const string ForestFill  = "#BEDDA8";
    public const string ShrubFill   = "#C7D8A5";
    public const string MeadowFill  = "#E6E6B0";
    public const string SandFill    = "#F1E2B2";
    public const string RocksFill   = "#BFB6A6";
    public const string OrchardFill = "#D6E0B7";
    public const string FarmlandFill = "#E9DDB2";
    public const string CemeteryFill = "#D4D4D4";

    // Дороги (фоновые подложки + черные линии поверх)
    public const string RoadBg      = "#FFFFFF"; // белая кайма для толстых линий
    public const string RoadMain    = "#111111";
    public const string RoadPrimary = "#222222";
    public const string RoadSecondary = "#333333";
    public const string RoadTertiary  = "#555555";
    public const string RoadLocal     = "#7B7B7B";
    public const string RoadService   = "#9E9E9E";

    // Застройка
    public const string BuildingFill = "#E0C8A0";
    public const string BuildingStroke = "#7A5C30";

    // Горизонтали
    public const string ContourStroke = "#A07040";
    public const string ContourIndexStroke = "#5C3D1F";
}