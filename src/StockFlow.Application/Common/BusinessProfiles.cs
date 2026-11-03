namespace StockFlow.Application.Common;

// Profiles constrain capabilities; they never grant user permissions.
public static class BusinessProfiles
{
    public const string Electrical = "ELECTRICAL";
    public const string ElectricalName = "Electrical / Electronics";
    public const string BikeAccessories = "BIKE_ACCESSORIES";
    public const string Clothing = "CLOTHING";
    public const string Footwear = "FOOTWEAR";

    public static readonly string[] Codes = [Electrical, BikeAccessories, Clothing, Footwear];
    public static bool IsSupported(string code) => Codes.Contains(code, StringComparer.Ordinal);
    public static string Name(string code) => code switch
    {
        Electrical => ElectricalName,
        BikeAccessories => "Bike Accessories",
        Clothing => "Clothing",
        Footwear => "Footwear / Chappal Shop",
        _ => "Unsupported profile"
    };
    public static string[] StarterCategories(string code) => code switch
    {
        BikeAccessories => ["Helmets", "Bike Spare Parts", "Side Guards", "Head Lights", "Mirrors", "Brake Parts", "Chains & Sprockets", "Gloves", "Bike Covers", "Bike Luggage"],
        Clothing => ["Shirts", "T-Shirts", "Trousers", "Dresses", "Sarees"],
        Footwear => ["Slippers", "Sandals", "Shoes"],
        _ => [] // Never seed or rewrite the existing electrical catalog.
    };
    public static bool AllowsModule(string code, string module)
    {
        if (!IsSupported(code)) return false;
        return code == Electrical || !(module.StartsWith("SERVICE", StringComparison.Ordinal)
            || module.StartsWith("WARRANTY", StringComparison.Ordinal)
            || module is "PENDING_SERVICES" or "EXPIRING_WARRANTIES");
    }
    public static bool SupportsSerials(string code) => code == Electrical;
    public static bool SupportsWarranty(string code) => code == Electrical;
    public static bool IsDemoProfile(string code) => code is Electrical or BikeAccessories;

    public static string ResolveLegacyCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code) ? Electrical : code.Trim();
    }
}
