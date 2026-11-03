using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class TransferRepository : ITransferRepository
{
    private readonly DbConnectionFactory factory;
    // ASP.NET supplies these services when it creates this class.
    public TransferRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Header = """
        SELECT h.transfer_id AS TransferId,h.transfer_number AS TransferNumber,h.status AS Status,
        h.from_location_id AS FromLocationId,h.to_location_id AS ToLocationId,
        h.requested_at AS RequestedAt,h.remarks AS Remarks,
        COALESCE((SELECT location_name FROM store_locations WHERE location_id=h.from_location_id),'Location #'||h.from_location_id) AS FromLocation,
        COALESCE((SELECT location_name FROM store_locations WHERE location_id=h.to_location_id),'Location #'||h.to_location_id) AS ToLocation
        FROM stock_transfers h
        """;
    private const string Visible = "(sf_location()=0 OR sf_location() IN(h.from_location_id,h.to_location_id)) AND (sf_store()=0 OR EXISTS(SELECT 1 FROM store_locations l WHERE l.store_id=sf_store() AND l.location_id IN(h.from_location_id,h.to_location_id)))";
    public async Task<IEnumerable<TransferResponse>> ListAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync<TransferResponse>(Header + " WHERE " + Visible + " ORDER BY h.transfer_id DESC LIMIT 200");
    }

    public async Task<object> LocationsAsync()
    {
        using var c = factory.CreateConnection();
        return await c.QueryAsync("SELECT location_id AS \"locationId\",store_name AS \"storeName\",location_name AS \"locationName\" FROM sf_transfer_locations()");
    }

    public async Task<IEnumerable<TransferUnit>> DispatchUnitsAsync(long id, long lineId, string? term, int offset)
    {
        if (offset < 0)
        {
            throw new ValidationException("Invalid unit page.");
        }

        factory.RequireLocation();
        using var c = factory.CreateConnection();
        return await c.QueryAsync<TransferUnit>("""
            SELECT i.transfer_item_id TransferItemId,u.product_unit_id ProductUnitId,u.serial_number SerialNumber,
                ARRAY[u.serial_number::text,u.barcode::text,u.internal_barcode::text,u.manufacturer_barcode::text] Barcodes
            FROM stock_transfers h JOIN stock_transfer_items i USING(tenant_id,transfer_id)
            JOIN product_units u ON u.variant_id=i.variant_id AND u.tenant_id=i.tenant_id
            WHERE h.transfer_id=@id AND i.transfer_item_id=@lineId AND h.status='APPROVED' AND h.from_location_id=sf_location()
                AND sf_transfer_permission('STOCK_TRANSFER','DISPATCH') AND u.unit_status='IN_STOCK' AND u.sale_id IS NULL
                AND (@term IS NULL OR u.serial_number ILIKE @term OR u.barcode ILIKE @term OR u.internal_barcode ILIKE @term OR u.manufacturer_barcode ILIKE @term)
            ORDER BY u.product_unit_id LIMIT 100 OFFSET @offset
            """,
new
{
    id,
    lineId,
    term = string.IsNullOrWhiteSpace(term) ? null : "%" + term.Trim() + "%",
    offset
});
    }

    public async Task<TransferResponse?> GetAsync(long id)
    {
        using var c = factory.CreateConnection();
        await c.OpenAsync();
        using var t = c.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);
        var result = await c.QuerySingleOrDefaultAsync<TransferResponse>(Header + " WHERE h.transfer_id=@id AND " + Visible, new
        {
            id
        }, t);
        if (result is null)
        {
            return null;
        }

        result.Items = (await c.QueryAsync<TransferLine>("""
            SELECT i.transfer_item_id AS TransferItemId,i.variant_id AS VariantId,p.product_name||' / '||v.variant_name AS ProductName,
            v.attributes::text AS AttributesJson,v.is_serial_tracked AS IsSerialTracked,i.requested_quantity AS RequestedQuantity,i.dispatched_quantity AS DispatchedQuantity,
            i.received_quantity AS ReceivedQuantity,i.damaged_quantity AS DamagedQuantity,i.lost_quantity AS LostQuantity
            FROM stock_transfer_items i JOIN product_variants v ON v.variant_id=i.variant_id JOIN products p ON p.product_id=v.product_id WHERE i.transfer_id=@id ORDER BY i.transfer_item_id
            """,
new
{
    id
},
t)).ToList();
        var units = (await c.QueryAsync<TransferUnit>("SELECT transfer_item_id AS TransferItemId,product_unit_id AS ProductUnitId,serial_number AS SerialNumber,resolution AS Resolution,barcodes AS Barcodes FROM sf_transfer_unit_labels(@id)",
new
{
    id
},
t)).ToLookup(x => x.TransferItemId);
        foreach (var item in result.Items)
        {
            item.Units = units[item.TransferItemId].ToList();
        }

        result.Operations = (await c.QueryAsync("SELECT action_name AS action,created_at AS date,payload FROM stock_transfer_operations WHERE transfer_id=@id ORDER BY created_at",
new
{
    id
},
t)).ToList();
        t.Commit();
        return result;
    }

    public async Task<long> CreateAsync(CreateTransferRequest r)
    {
        factory.RequireLocation();
        if (factory.Scope is { } scope && scope.LocationId != r.FromLocationId && scope.LocationId != r.ToLocationId)
        {
            throw new ValidationException("Use your current location as the source or destination.");
        }

        if (r.RequestId == Guid.Empty || r.Items is null || r.Items.Count is < 1 or > 200 || r.Items.Any(i => i is null || i.VariantId <= 0 || i.Quantity is < 1 or > 10000))
        {
            throw new ValidationException("Choose 1 to 200 valid variant quantities and a request ID.");
        }

        using var c = factory.CreateConnection();
        return await c.ExecuteScalarAsync<long>("SELECT sf_create_transfer(@FromLocationId,@ToLocationId,CAST(@lines AS jsonb),@Notes,@RequestId)",
new
{
    r.FromLocationId,
    r.ToLocationId,
    lines = JsonSerializer.Serialize(r.Items, Json),
    r.Notes,
    r.RequestId
});
    }

    public async Task<long> ActAsync(long id, string action, TransferActionRequest r)
    {
        factory.RequireLocation();
        if (action is not ("request" or "approve" or "dispatch" or "receive" or "discrepancy" or "cancel") || r.RequestId == Guid.Empty)
        {
            throw new ValidationException("Invalid action or request ID.");
        }

        using var c = factory.CreateConnection();
        return await c.ExecuteScalarAsync<long>("SELECT sf_transfer_action(@id,@action,CAST(@body AS jsonb),@RequestId)",
new
{
    id,
    action,
    body = JsonSerializer.Serialize(r, Json),
    r.RequestId
});
    }
}
