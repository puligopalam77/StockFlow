namespace StockFlow.Application.Common;

public static class FieldPatterns
{
    public const string Phone = "^[0-9]{10}$";
    public const string PhoneMessage = "Enter exactly 10 digits, without spaces or a country code.";
    public const string Gst = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$";
    public const string GstMessage = "Enter a valid 15-character GSTIN.";
}
