using System.ComponentModel.DataAnnotations;
using Dapper;
using Npgsql;
using StockFlow.Application.DTOs.Service;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

// Invoked only by SalesDatabaseChecks, which requires the explicitly configured disposable database.
internal static class ServiceDatabaseChecks
{
    public static async Task Run(DbConnectionFactory factory,NpgsqlConnection c)
    {
        var warranties=new WarrantyRepository(factory);
        var service=new ServiceService(new ServiceRepository(factory,warranties));
        var warranty=(await warranties.GetByUnitAsync(5))!;
        await c.ExecuteAsync("UPDATE warranties SET warranty_start_date=CURRENT_DATE-20,warranty_end_date=CURRENT_DATE-1 WHERE warranty_id=@id",new{id=warranty.WarrantyId});
        var beforeStock=await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=1");
        var request=new CreateServiceRequest{RequestId=Guid.NewGuid(),CustomerId=1,ProductId=1,ProductUnitId=5,WarrantyId=warranty.WarrantyId,Complaint="Customer-owned battery repair",AssignedTo=3};
        var ids=await Task.WhenAll(service.CreateAsync(request,3),service.CreateAsync(request,3));
        var ticket=(await service.GetByIdAsync(ids[0]))!;
        Check(ids[0]==ids[1] && ticket.Status=="RECEIVED" && !ticket.IsUnderWarranty && ticket.UnderWarrantyAtReceipt==false,"expired-warranty intake and idempotent create");
        Check(await c.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=5")=="IN_SERVICE","intake unit status");
        Check(await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=1")==beforeStock,"customer intake does not change shop stock");
        request.RequestId=Guid.NewGuid();
        await Reject(()=>service.CreateAsync(request,3));
        await Reject(()=>service.UpdateStatusAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,Status="DELIVERED",Remarks="Invalid jump"},3));
        await Status("INSPECTION");
        var part=new AddServicePartRequest{RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,ProductId=2,Quantity=10000,UnitPrice=10};
        var before=await Snapshot(c);
        await Reject(()=>service.AddPartAsync(ticket.ServiceRequestId,part,3));
        Check(await Snapshot(c)==before,"insufficient stock leaves ticket, parts, history and stock unchanged");
        part.RequestId=Guid.NewGuid(); part.Quantity=2;
        var partStock=await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=2");
        var parts=await Task.WhenAll(service.AddPartAsync(ticket.ServiceRequestId,part,3),service.AddPartAsync(ticket.ServiceRequestId,part,3));
        Check(parts[0]==parts[1] && await c.ExecuteScalarAsync<long>("SELECT current_stock FROM vw_current_stock WHERE product_id=2")==partStock-2,"part retries consume stock once");
        Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM inventory_transactions WHERE reference_type='SERVICE_PART' AND reference_id=@id AND transaction_type='SERVICE_OUT'",new{id=parts[0]})==1,"part movement audit");
        ticket=(await service.GetByIdAsync(ticket.ServiceRequestId))!;
        Check(ticket.PartsTotal==20,"part charge total");
        await Reject(()=>service.UpdateAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=1,ServiceCharge=25},3));
        await Reject(()=>service.UpdateStatusAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,Status="CANCELLED",Remarks="Parts already consumed"},3));
        await service.UpdateAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,Diagnosis="Fault found",RepairDetails="Fuse replaced",ServiceCharge=25,AssignedTo=3},3);
        ticket=(await service.GetByIdAsync(ticket.ServiceRequestId))!;
        Check(ticket.TotalCharge==45,"labour and parts remain separate");
        await Status("WAITING_FOR_PART"); await Status("REPAIRING"); await Status("READY"); await Status("DELIVERED");
        Check(ticket.DeliveredDate.HasValue && await c.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=5")=="REPLACEMENT_ISSUED","delivery restores original owned-unit status");
        await Reject(()=>service.UpdateAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,ServiceCharge=0},3));
        Check((await service.GetHistoryAsync(ticket.ServiceRequestId,0)).Count()==8,"every create/update/part/status has retained history");
        Check((await service.GetAllAsync(new(){Status="DELIVERED",Term=ticket.ServiceNumber})).Any(),"completed service search");
        Check(!(await service.GetAllAsync(new(){PendingOnly=true,Term=ticket.ServiceNumber})).Any(),"delivered ticket excluded from pending");
        var cancelId=await service.CreateAsync(new(){RequestId=Guid.NewGuid(),CustomerId=1,ProductId=1,ProductUnitId=3,Complaint="Cancel before work"},3);
        await service.UpdateStatusAsync(cancelId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=1,Status="CANCELLED",Remarks="Customer withdrew request"},3);
        Check(await c.ExecuteScalarAsync<string>("SELECT unit_status FROM product_units WHERE product_unit_id=3")=="SOLD","cancellation releases the customer unit");
        Check((await service.GetTechniciansAsync()).All(t=>t.UserId!=2),"Sales users are not technicians");
        Console.WriteLine("PASS: service intake, expired coverage, status flow, stock issuance, concurrent retries, rollback, totals, delivery and cancellation.");

        async Task Status(string status)
        {
            await service.UpdateStatusAsync(ticket.ServiceRequestId,new(){RequestId=Guid.NewGuid(),ExpectedVersion=ticket.Version,Status=status,Remarks="Test "+status},3);
            ticket=(await service.GetByIdAsync(ticket.ServiceRequestId))!;
        }
    }
    private static Task<string> Snapshot(NpgsqlConnection c)=>c.QuerySingleAsync<string>("""
        SELECT jsonb_build_object(
            'tickets',(SELECT jsonb_agg(to_jsonb(x) ORDER BY service_request_id) FROM service_requests x),
            'parts',(SELECT jsonb_agg(to_jsonb(x) ORDER BY service_part_id) FROM service_parts x),
            'history',(SELECT jsonb_agg(to_jsonb(x) ORDER BY service_history_id) FROM service_history x),
            'units',(SELECT jsonb_agg(to_jsonb(x) ORDER BY product_unit_id) FROM product_units x),
            'movements',(SELECT jsonb_agg(to_jsonb(x) ORDER BY inventory_transaction_id) FROM inventory_transactions x),
            'retries',(SELECT jsonb_agg(to_jsonb(x) ORDER BY request_id) FROM service_operations x))::text
        """);
    private static async Task Reject(Func<Task> action) { try { await action(); } catch(ValidationException) { return; } throw new Exception("Expected service validation rejection."); }
    private static void Check(bool ok,string text) { if(!ok) throw new Exception("FAIL: "+text); }
}
