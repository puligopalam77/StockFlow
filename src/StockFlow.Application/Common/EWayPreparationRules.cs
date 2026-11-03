using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.Common;

// Preparation checks only. No universal value threshold, statutory clearance or locally calculated validity.
public static class EWayPreparationRules
{
    public const string Version = "preparation-2026-11-02-nic-v1.03-review";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public static void Shape(EWaySave input)
    {
        if (input.DocumentId == Guid.Empty || input.OperationId == Guid.Empty || input.ExpectedVersion < 0 || input.SourceId <= 0 || input.RegistrationId <= 0
            || input.SourceType is not ("GST" or "TRANSFER") || input.Preparation is null)
            throw new ValidationException("Select a persisted source and valid request identity.");
        var p = input.Preparation;
        if (p.BillFrom is null || p.DispatchFrom is null || p.BillTo is null || p.ShipTo is null || p.TransferGoods is null || p.TransferGoods.Count > 200
            || JsonSerializer.Serialize(p, Json).Length > 100000)
            throw new ValidationException("Preparation data is incomplete or too large.");
        foreach (var party in new[] { p.BillFrom, p.DispatchFrom, p.BillTo, p.ShipTo })
            if (party.Name is null || party.Address is null || party.Gstin is null || party.Pin is null || party.State is null || party.Name.Length > 200 || party.Address.Length > 1000 || party.Gstin.Length > 15 || party.Pin.Length > 6 || party.State.Length > 2)
                throw new ValidationException("Party fields exceed the supported limits.");
        if (input.SourceType == "GST" && p.TransferGoods.Count != 0) throw new ValidationException("GST source goods and values are immutable; do not supply replacement lines.");
        if (!string.IsNullOrEmpty(p.MovementDocumentDate) && !DateOnly.TryParseExact(p.MovementDocumentDate, "yyyy-MM-dd", out _)) throw new ValidationException("Enter a valid actual document date.");
    }
    public static EWayValidation Validate(EWayPreparation p, JsonElement source)
    {
        var issues = new List<string>();
        foreach (var (party, name) in new[] { (p.BillFrom, "Bill from"), (p.DispatchFrom, "Dispatch from"), (p.BillTo, "Bill to"), (p.ShipTo, "Ship to") })
        {
            if (party.Name.Trim().Length < 2 || party.Address.Trim().Length < 5) issues.Add(name + ": enter the actual name and address.");
            if (!GstRules.States.ContainsKey(party.State)) issues.Add(name + ": choose a supported state code.");
            if (!Regex.IsMatch(party.Pin, "^[1-9][0-9]{5}$")) issues.Add(name + ": enter the actual six-digit Indian PIN; overseas/SEZ cases require separate review.");
            if (party.Gstin != "URP" && !GstRules.ValidGstin(party.Gstin)) issues.Add(name + ": enter a valid GSTIN or explicitly enter URP for an unregistered party.");
            else if (party.Gstin != "URP" && !party.Gstin.StartsWith(party.State, StringComparison.Ordinal)) issues.Add(name + ": GSTIN and state must agree.");
        }
        if (p.TransactionType is < 1 or > 4) issues.Add("Choose the bill/ship/dispatch transaction arrangement.");
        if (p.DocumentType is not ("INV" or "BIL" or "CHL" or "OTH" or "BOE")) issues.Add("Choose a document type from the reviewed official master codes.");
        if (string.IsNullOrWhiteSpace(p.MovementReason)) issues.Add("Record the actual movement reason and review its official subtype.");
        if (string.IsNullOrWhiteSpace(p.MovementDocumentNumber) || !DateOnly.TryParseExact(p.MovementDocumentDate, "yyyy-MM-dd", out _)) issues.Add("Enter the actual movement document number and date.");
        if (p.DistanceKm is null or < 0 or > 4000) issues.Add("Enter the actual distance (0-4000 km); portal PIN/distance validation is still required.");
        if (p.Mode is not ("ROAD" or "RAIL" or "AIR" or "SHIP" or "PART_A")) issues.Add("Choose a supported preparation transport mode.");
        if (p.Mode == "ROAD" && (string.IsNullOrWhiteSpace(p.Vehicle) || !Regex.IsMatch(p.Vehicle, "^[A-Z0-9]{6,15}$"))) issues.Add("Enter the actual road vehicle registration; the portal performs final format validation.");
        if (p.Mode == "PART_A" && (string.IsNullOrWhiteSpace(p.TransporterId) || !string.IsNullOrWhiteSpace(p.Vehicle) || !string.IsNullOrWhiteSpace(p.TransportDocument))) issues.Add("Part A preparation needs a transporter ID and no vehicle/transport document.");
        if (p.Mode is "RAIL" or "AIR" or "SHIP" && (string.IsNullOrWhiteSpace(p.TransportDocument) || !DateOnly.TryParseExact(p.TransportDocumentDate, "yyyy-MM-dd", out _))) issues.Add("Enter the transport document number and date for this mode.");
        var transfer = source.GetProperty("type").GetString() == "TRANSFER";
        var inward = source.TryGetProperty("direction", out var direction) && direction.GetString() == "PURCHASE";
        var registration = source.GetProperty("registration");
        var issuerGstin = registration.TryGetProperty("gstin", out var gstin) && gstin.ValueKind == JsonValueKind.String ? gstin.GetString() : null;
        if (string.IsNullOrEmpty(issuerGstin) || (inward ? p.BillTo.Gstin : p.BillFrom.Gstin) != issuerGstin)
            issues.Add("The issuing/receiving business GSTIN must match the source registration snapshot. Unregistered issuer cases require separate portal review.");
        if (!transfer && source.TryGetProperty("partyGstin", out var partyGstin) && partyGstin.ValueKind == JsonValueKind.String
            && (inward ? p.BillFrom.Gstin : p.BillTo.Gstin) != partyGstin.GetString()) issues.Add("The known counterparty GSTIN must match the issued source snapshot.");
        if (!transfer && (p.MovementDocumentNumber != source.GetProperty("number").GetString() || p.MovementDocumentDate != source.GetProperty("date").GetString()))
            issues.Add("Movement document identity must match the issued source invoice; revise the linked source selection instead of changing historical identity.");
        var lines = transfer ? p.TransferGoods : source.GetProperty("lines").Deserialize<List<GstLine>>(Json) ?? [];
        if (lines.Count == 0) issues.Add("Goods lines are missing.");
        if (transfer && (string.IsNullOrWhiteSpace(p.GoodsEvidence) || p.GoodsEvidence.Trim().Length < 10 || p.DocumentType != "CHL")) issues.Add("Transfer preparation requires an actual delivery challan and evidence for its goods/values; a transfer is not a sales invoice.");
        if (lines.Any(l => l is null || l.Classification != "GOODS" || !Regex.IsMatch(l.HsnSac ?? "", "^[0-9]{4}([0-9]{2})?([0-9]{2})?$") || l.Quantity <= 0 || l.TaxableValue < 0 || !Regex.IsMatch(l.Uqc ?? "", "^[A-Z]{3}$"))) issues.Add("Review goods classification, HSN, quantity, UQC and source values. Services are not goods movement.");
        if (transfer && lines.Any(l => l is not null && (l.Cgst < 0 || l.Sgst < 0 || l.Utgst < 0 || l.Igst < 0 || l.Cess < 0 || l.Total != l.TaxableValue + l.Cgst + l.Sgst + l.Utgst + l.Igst + l.Cess))) issues.Add("Actual challan values must be non-negative and reconcile; no cost or tax rate is inferred.");
        if (string.IsNullOrWhiteSpace(p.ApplicabilityReview) || p.ApplicabilityReview.Trim().Length < 10) issues.Add("Record authorized applicability review, including state/movement/exemption conditions. Value alone is insufficient.");
        return new(Version, "Review Required", false, issues.ToArray(),
            ["Preparation checks are not statutory clearance. Verify current official/state rules and source changes in the portal.",
             "No authorized API adapter is installed. Worksheet export or manual EBN entry does not confirm government generation."]);
    }
}
