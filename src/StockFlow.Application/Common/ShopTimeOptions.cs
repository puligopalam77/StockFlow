namespace StockFlow.Application.Common;

public class ShopTimeOptions
{
    public string TimeZoneId { get; set; } = "Asia/Kolkata";

    public DateTime Today(TimeProvider clock)
    {
        return TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)).Date;
    }
}
