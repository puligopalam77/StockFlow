using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Application.Common;

public static class CashInHandRules
{
    public const string OpeningReason = "Opening cash counted at initialization";
    public static readonly IReadOnlyDictionary<string,(string Kind,string Label,bool Explanation)> EntryReasons =
        new Dictionary<string,(string,string,bool)>{
            ["OWNER_CONTRIBUTION"]=("OWNER_IN","Owner Contribution",false),
            ["OTHER_INFLOW"]=("OWNER_IN","Other Cash Inflow",true),
            ["OWNER_WITHDRAWAL"]=("OWNER_OUT","Owner Withdrawal",false),
            ["OTHER_OUTFLOW"]=("OWNER_OUT","Other Cash Outflow",true)
        };
    public static void Validate(CashAdjustmentRequest request, DateTime today, bool allowLegacyBankReplay = false, bool allowLegacyManualReplay = false)
    {
        request.Reason = request.Reason?.Trim() ?? "";
        if(request.Kind=="OPENING" && request.Reason.Length==0)request.Reason=OpeningReason;
        var manual=request.Kind is "OWNER_IN" or "OWNER_OUT";
        if(request.Note is not null)
        {
            request.Note=request.Note.Trim();
            if((!manual&&request.Kind!="OPENING") || request.Note.Length>(manual?250:1000) || request.Note.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t')))
                throw new ValidationException(manual?"Enter a plain-text note of at most 250 characters.":"Opening notes must be plain text of at most 1,000 characters.");
        }
        if(manual && !(allowLegacyManualReplay&&request.ReasonCode is null&&request.Note is null))
        {
            if(request.ReasonCode is null || !EntryReasons.TryGetValue(request.ReasonCode,out var reason) || reason.Kind!=request.Kind)
                throw new ValidationException("Choose a valid reason for the selected cash entry type.");
            request.Reason=reason.Label; // Never accept a client-supplied display label.
            if(reason.Explanation && ((request.Note?.Length??0)<3 || !request.Note!.Any(char.IsLetterOrDigit)))
                throw new ValidationException("Explain the other cash movement in the Note field (3–250 characters).");
        }
        else if(!manual&&request.ReasonCode is not null)throw new ValidationException("Reason codes apply only to manual cash entries.");
        var legacyBank = allowLegacyBankReplay && request.Kind is ("BANK_DEPOSIT" or "BANK_WITHDRAWAL");
        if (request.RequestId == Guid.Empty || !legacyBank && request.Kind is not ("OPENING" or "OWNER_IN" or "OWNER_OUT" or "REVERSAL"))
            throw new ValidationException("Choose a supported non-trading cash movement.");
        if (request.Amount < (request.Kind == "OPENING" ? 0 : 0.01m) || request.Amount > 9999999999.99m || decimal.Round(request.Amount, 2) != request.Amount)
            throw new ValidationException("Enter a valid cash amount with at most two decimal places.");
        if (request.Reason.Length is < 3 or > 1000)
            throw new ValidationException("Enter a reason between 3 and 1,000 characters.");
        if (request.Kind == "OPENING")
        {
            if(request.OpeningMode=="CURRENT")
            {
                if(request.StartDate is not null)throw new ValidationException("Current drawer opening uses the recorded initialization time, not a backdated start date.");
            }
            else if(request.OpeningMode is not null || request.StartDate is null || request.StartDate.Value.Date > today.Date || request.StartDate.Value.Year < 2000)
                throw new ValidationException("Choose the date cash tracking starts. Opening is the cash before that day's payments.");
            if(request.StartDate is {} start)request.StartDate=start.Date;
        }
        else if (request.StartDate is not null || request.OpeningMode is not null)
            throw new ValidationException("Only opening cash can set a tracking start date.");
        if ((request.Kind == "REVERSAL") != request.ReversesId.HasValue || request.ReversesId == Guid.Empty)
            throw new ValidationException("A correction must identify the original cash adjustment.");
    }
}
