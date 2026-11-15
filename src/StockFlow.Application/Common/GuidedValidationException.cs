using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.Common;

// A fixed code accompanies existing validation messages; no model infers the cause.
public sealed class GuidedValidationException(string code, string message) : ValidationException(message)
{
    public string Code { get; } = code;
}
