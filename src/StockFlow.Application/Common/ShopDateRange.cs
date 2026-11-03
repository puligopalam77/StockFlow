using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Common;

public record ShopDateRange(DateTime From, DateTime To, DateTime StartUtc, DateTime EndUtc)
{
    public static ShopDateRange Resolve(SalesPeriod period, ShopTimeOptions options, TimeProvider clock)
    {
        var today = options.Today(clock);
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var month = new DateTime(today.Year, today.Month, 1);
        var (from, to) = period.Period switch {
            "current" or "today" => (today, today),
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "day-before-yesterday" => (today.AddDays(-2), today.AddDays(-2)),
            "this-week" => (monday, today), "last-week" => (monday.AddDays(-7), monday.AddDays(-1)),
            "this-month" => (month, today), "last-month" => (month.AddMonths(-1), month.AddDays(-1)),
            "range" when period.From.HasValue && period.To.HasValue => (period.From.Value.Date, period.To.Value.Date),
            _ => throw new ArgumentException("Unsupported period.")
        };
        if (from > to || (to-from).TotalDays > 366 || to == DateTime.MaxValue.Date)
            throw new ArgumentException("Choose a date range of up to 367 days.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        return new(from, to, TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.AddDays(1), DateTimeKind.Unspecified), zone));
    }
}
