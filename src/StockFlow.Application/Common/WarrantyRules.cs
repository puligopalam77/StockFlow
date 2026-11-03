using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Warranty;

namespace StockFlow.Application.Common;

public static class WarrantyRules
{
    public static readonly string[] OpenStatuses = ["OPEN", "UNDER_REVIEW", "APPROVED"];
    public static string Code(string value)
    {
        value = value?.Trim() ?? "";
        if (value.Length is < 1 or > 200 || value.Any(char.IsControl))
        {
            throw new ValidationException("Enter a valid serial or invoice number (maximum 200 characters).");
        }

        return value;
    }

    public static void Validate(CreateWarrantyClaimRequest request)
    {
        if (request.RequestId == Guid.Empty || request.WarrantyId <= 0)
        {
            throw new ValidationException("A valid warranty and retry request ID are required.");
        }

        request.Complaint = request.Complaint?.Trim() ?? "";
        if (request.Complaint.Length is < 1 or > 1000)
        {
            throw new ValidationException("Enter a complaint of up to 1,000 characters.");
        }
    }

    public static void Validate(ResolveWarrantyClaimRequest request)
    {
        if (request.RequestId == Guid.Empty || !OpenStatuses.Contains(request.ExpectedStatus))
        {
            throw new ValidationException("A request ID and current open claim status are required.");
        }

        if (request.ClaimStatus is not ("UNDER_REVIEW" or "APPROVED" or "REJECTED" or "REPLACED" or "CLOSED"))
        {
            throw new ValidationException("Choose a supported claim status.");
        }

        request.ResolutionNotes = request.ResolutionNotes?.Trim();
        if (string.IsNullOrWhiteSpace(request.ResolutionNotes) || request.ResolutionNotes.Length > 2000)
        {
            throw new ValidationException("Enter update/resolution notes (maximum 2,000 characters).");
        }

        if (request.ClaimStatus == "REPLACED")
        {
            if (request.ExpectedStatus != "APPROVED" || request.ResolutionType != "REPLACEMENT" || request.ReplacementProductUnitId is null or <= 0)
            {
                throw new ValidationException("Approve the claim first, then select an available replacement unit.");
            }

            if (request.ReplacementWarrantyRule is not ("CONTINUE_ORIGINAL" or "NEW_FULL_WARRANTY" or "CUSTOM"))
            {
                throw new ValidationException("Explicitly choose the replacement warranty rule.");
            }

            if ((request.ReplacementWarrantyRule == "CUSTOM") != request.CustomWarrantyEndDate.HasValue)
            {
                throw new ValidationException("A custom end date is required only for custom coverage.");
            }
        }
        else
        {
            if (request.ReplacementProductUnitId.HasValue || request.ReplacementWarrantyRule is not null || request.CustomWarrantyEndDate.HasValue)
            {
                throw new ValidationException("Replacement details apply only when issuing a replacement.");
            }

            if (request.ClaimStatus == "REJECTED" && request.ResolutionType != "REJECTED")
            {
                throw new ValidationException("Select the rejected resolution.");
            }

            if (request.ClaimStatus == "CLOSED" && request.ResolutionType is not ("REPAIR" or "NO_FAULT_FOUND" or "MANUFACTURER_REFERRED"))
            {
                throw new ValidationException("Choose a closure resolution.");
            }

            if (request.ClaimStatus == "CLOSED" && request.ResolutionType == "REPAIR" && request.ExpectedStatus != "APPROVED")
            {
                throw new ValidationException("Approve the claim before closing a repair.");
            }

            if (request.ClaimStatus is "UNDER_REVIEW" or "APPROVED" && request.ResolutionType is not null)
            {
                throw new ValidationException("Progress updates must not include a final resolution.");
            }
        }

        if (request.ExpectedStatus == request.ClaimStatus || (request.ClaimStatus == "UNDER_REVIEW" && request.ExpectedStatus != "OPEN"))
        {
            throw new ValidationException("This claim transition is not allowed.");
        }
    }

    public static void Validate(WarrantyCoverageRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new ValidationException("A retry request ID is required.");
        }

        if (request.WarrantyType is not (null or "FULL_REPLACEMENT" or "PRO_RATA" or "REPAIR" or "SERVICE_ONLY") || request.WarrantyProvider is not (null or "MANUFACTURER" or "DEALER" or "BOTH"))
        {
            throw new ValidationException("Choose a supported warranty type and provider.");
        }

        request.Reason = request.Reason?.Trim() ?? "";
        if (request.Reason.Length is < 1 or > 1000)
        {
            throw new ValidationException("A reason of up to 1,000 characters is required.");
        }
    }
}
