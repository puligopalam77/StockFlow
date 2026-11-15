namespace StockFlow.Web.Help;

public static class StockBotErrorCodes
{
    public static string? ForStatus(int status) => status switch
    {
        400 or 422 => "VALIDATION_ERROR",
        401 => "AUTH_REQUIRED",
        403 => "PERMISSION_DENIED",
        404 => "NOT_FOUND",
        409 => "CONFLICT",
        429 => "RATE_LIMITED",
        >= 500 => "SERVICE_UNAVAILABLE",
        _ => null
    };
}
