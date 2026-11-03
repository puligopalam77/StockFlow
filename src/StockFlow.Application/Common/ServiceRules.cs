using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Service;

namespace StockFlow.Application.Common;

public static class ServiceRules
{
    public static readonly string[] Statuses = ["RECEIVED", "INSPECTION", "REPAIRING", "WAITING_FOR_PART", "READY", "DELIVERED", "CANCELLED"];
    public static string[] Transitions(string status)
    {
        return status switch
        {
            "RECEIVED" => ["INSPECTION", "CANCELLED"],
            "INSPECTION" => ["REPAIRING", "WAITING_FOR_PART", "READY", "CANCELLED"],
            "REPAIRING" => ["WAITING_FOR_PART", "READY"],
            "WAITING_FOR_PART" => ["REPAIRING", "READY"],
            "READY" => ["DELIVERED"],
            _ => []
        };
    }

    public static bool IsTerminal(string status)
    {
        return status is "DELIVERED" or "CANCELLED";
    }

    public static void Transition(string current, string next)
    {
        if (!Transitions(current).Contains(next))
        {
            throw new ValidationException("This service status transition is not allowed.");
        }
    }

    static string Text(string? value, int max, bool required = false)
    {
        var result = value?.Trim() ?? "";
        if ((required && result.Length == 0) || result.Length > max)
        {
            throw new ValidationException($"Enter {(required ? "non-empty " : "")}text of up to {max} characters.");
        }

        return result;
    }

    public static void Money(decimal value)
    {
        if (value < 0 || value > 9999999999.99m || decimal.Round(value, 2) != value)
        {
            throw new ValidationException("Enter a non-negative amount with at most two decimal places, up to 9,999,999,999.99.");
        }
    }

    static void Retry(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ValidationException("A retry request ID is required.");
        }
    }

    static void Mutation(ServiceMutation r)
    {
        Retry(r.RequestId);
        if (r.ExpectedVersion <= 0)
        {
            throw new ValidationException("Reload the ticket before changing it.");
        }
    }

    static void Assignment(int? id)
    {
        if (id <= 0)
        {
            throw new ValidationException("Choose a valid technician.");
        }
    }

    public static void Validate(CreateServiceRequest r)
    {
        Retry(r.RequestId);
        Assignment(r.AssignedTo);
        if (r.CustomerId <= 0 || r.ProductId <= 0 || r.ProductUnitId <= 0 || r.WarrantyId <= 0)
        {
            throw new ValidationException("Choose a valid customer, product and related records.");
        }

        r.Complaint = Text(r.Complaint, 1000, true);
        r.Notes = Text(r.Notes, 2000);
        r.ExpectedDeliveryDate = r.ExpectedDeliveryDate?.Date;
    }

    public static void Validate(UpdateServiceRequest r)
    {
        Mutation(r);
        Assignment(r.AssignedTo);
        Money(r.ServiceCharge);
        r.Diagnosis = Text(r.Diagnosis, 4000);
        r.RepairDetails = Text(r.RepairDetails, 4000);
        r.Notes = Text(r.Notes, 2000);
        r.ExpectedDeliveryDate = r.ExpectedDeliveryDate?.Date;
    }

    public static void Validate(UpdateServiceStatusRequest r)
    {
        Mutation(r);
        r.Status = Text(r.Status, 30, true).ToUpperInvariant();
        r.Remarks = Text(r.Remarks, 1000, true);
        if (!Statuses.Contains(r.Status))
        {
            throw new ValidationException("Choose a supported service status.");
        }
    }

    public static void Validate(AddServicePartRequest r)
    {
        Mutation(r);
        Money(r.UnitPrice);
        if (r.ProductId <= 0 || r.ProductUnitId <= 0 || r.Quantity is < 1 or > 10000 || (r.ProductUnitId.HasValue && r.Quantity != 1))
        {
            throw new ValidationException("Choose a part and quantity (1–10,000); a serialized part uses one unit per row.");
        }

        Money(r.UnitPrice * r.Quantity);
    }

    public static void Validate(ServiceSearch r)
    {
        r.Term = Text(r.Term, 200);
        if (r.Offset < 0 || (r.Status is not null && !Statuses.Contains(r.Status)) || r.From > r.To || (r.PendingOnly && r.Status is "DELIVERED" or "CANCELLED"))
        {
            throw new ValidationException("Choose valid service filters and dates.");
        }

        r.From = r.From?.Date;
        r.To = r.To?.Date;
    }
}
