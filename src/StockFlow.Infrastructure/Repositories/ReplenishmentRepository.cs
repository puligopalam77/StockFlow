using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Infrastructure.Repositories;
public sealed class ReplenishmentRepository(DbConnectionFactory factory,Microsoft.Extensions.Options.IOptions<ShopTimeOptions>? shopTime=null):IReplenishmentRepository
{
 public static readonly string[] Statuses=["REORDER_NOW","REVIEW_SOON","COVERED","SUFFICIENT","INSUFFICIENT_HISTORY","SUPPLIER_NOT_CONFIGURED","DATA_WARNING"];
 static void Days(int days){if(days is not(7 or 30 or 60 or 90))throw new ValidationException("Choose 7, 30, 60 or 90 days.");}
 async Task Guard(NpgsqlConnection c,NpgsqlTransaction? tx,string action)
 {
  factory.RequireLocation();
  if(!await c.ExecuteScalarAsync<bool>("SELECT sf_replenishment_allowed(@action)",new{action},tx))throw new UnauthorizedAccessException("Planning requires an authorized location and view permissions for inventory, sales, purchases, suppliers and transfers.");
 }
 public async Task<IReadOnlyList<ReplenishmentSupplier>> SuppliersAsync()
 {
  await using var c=factory.CreateConnection();await c.OpenAsync();await Guard(c,null,"VIEW");
  return (await c.QueryAsync<ReplenishmentSupplier>("SELECT i.variant_id VariantId,s.supplier_id SupplierId,s.supplier_name SupplierName,i.supplier_sku SupplierCode FROM variant_supplier_identifiers i JOIN suppliers s USING(tenant_id,supplier_id) JOIN product_variants v USING(tenant_id,variant_id) JOIN products p USING(tenant_id,product_id) WHERE s.is_active AND v.is_active AND p.is_active ORDER BY s.supplier_name,i.variant_id")).ToList();
 }
 public async Task<long> SavePolicyAsync(ReplenishmentPolicy p)
 {
  await using var c=factory.CreateConnection();await c.OpenAsync();await Guard(c,null,"POLICY_EDIT");
  return await c.ExecuteScalarAsync<long>("SELECT sf_replenishment_policy(@VariantId,@Version,@SupplierId,@LeadDays,@SafetyStock,@CoverageDays,@Moq,@OrderMultiple)",p);
 }
 // One aggregate per source and one consistent snapshot; no per-variant history queries.
 const string Inputs="""
 WITH demand AS(
 SELECT i.variant_id,sum(i.quantity)::bigint sold,count(DISTINCT s.sale_date::date)::int sale_days
 FROM sales s JOIN sale_items i USING(tenant_id,sale_id)
 WHERE s.tenant_id=sf_tenant() AND s.store_id=sf_store() AND s.location_id=sf_location() AND s.business_profile_code=sf_visible_business_profile()
 AND s.status='COMPLETED' AND i.variant_id IS NOT NULL AND i.product_id IS NOT NULL
 AND s.sale_date>=(@Cutoff AT TIME ZONE current_setting('TimeZone'))-make_interval(days=>@Days) AND s.sale_date<(@Cutoff AT TIME ZONE current_setting('TimeZone')) GROUP BY i.variant_id
 ),returned AS(
 SELECT i.variant_id,coalesce(sum(i.quantity) FILTER(WHERE i.disposition='RESTOCK'),0)::bigint restocked,
 coalesce(sum(i.quantity) FILTER(WHERE i.disposition<>'RESTOCK'),0)::bigint non_saleable
 FROM sale_returns r JOIN sale_return_items i USING(tenant_id,sale_return_id) JOIN sales s ON s.tenant_id=r.tenant_id AND s.sale_id=r.sale_id
 WHERE r.tenant_id=sf_tenant() AND s.store_id=sf_store() AND r.location_id=sf_location() AND s.business_profile_code=sf_visible_business_profile() AND s.status='COMPLETED'
 AND r.return_date>=@Cutoff-make_interval(days=>@Days) AND r.return_date<@Cutoff GROUP BY i.variant_id
 ),incoming AS(
 SELECT i.variant_id,coalesce(sum(greatest(0,i.quantity-i.received_quantity-i.damaged_quantity)) FILTER(WHERE p.status='PARTIALLY_RECEIVED'),0)::bigint open_po,
 coalesce(sum(i.quantity) FILTER(WHERE p.status='DRAFT'),0)::bigint draft_po
 FROM purchases p JOIN purchase_items i USING(tenant_id,purchase_id)
 WHERE p.tenant_id=sf_tenant() AND p.location_id=sf_location() AND p.business_profile_code=sf_visible_business_profile() GROUP BY i.variant_id
 ),transit AS(
 SELECT i.variant_id,sum(greatest(0,i.dispatched_quantity-i.received_quantity-i.damaged_quantity-i.lost_quantity))::bigint units
 FROM stock_transfers t JOIN stock_transfer_items i USING(tenant_id,transfer_id)
 WHERE t.tenant_id=sf_tenant() AND t.to_location_id=sf_location() AND t.business_profile_code=sf_visible_business_profile() AND t.status IN('DISPATCHED','PARTIALLY_RECEIVED') GROUP BY i.variant_id
 ),serials AS(SELECT variant_id,count(*)::bigint units FROM product_units WHERE tenant_id=sf_tenant() AND location_id=sf_location() AND unit_status='IN_STOCK' AND sale_id IS NULL GROUP BY variant_id)
 SELECT p.product_id ProductId,v.variant_id VariantId,p.product_name ProductName,v.variant_name VariantName,v.sku Sku,p.unit_of_measure UnitOfMeasure,v.is_serial_tracked IsSerialTracked,
 s.current_stock Stock,sf_variant_minimum(v.variant_id,v.minimum_stock_level) Minimum,coalesce(u.units,0) SerialUnits,
 coalesce(d.sold,0) Sold,coalesce(d.sale_days,0) SaleDays,greatest(0,least(@Days,extract(day FROM (@Cutoff AT TIME ZONE current_setting('TimeZone'))-p.created_at)::integer)) HistoryDays,
 coalesce(r.restocked,0) RestockedReturns,coalesce(r.non_saleable,0) NonSaleableReturns,coalesce(po.open_po,0) OpenPo,coalesce(po.draft_po,0) DraftPo,coalesce(t.units,0) IncomingTransfer
 FROM products p JOIN product_variants v USING(tenant_id,product_id) JOIN vw_variant_current_stock s ON s.variant_id=v.variant_id
 LEFT JOIN demand d ON d.variant_id=v.variant_id LEFT JOIN returned r ON r.variant_id=v.variant_id LEFT JOIN incoming po ON po.variant_id=v.variant_id LEFT JOIN transit t ON t.variant_id=v.variant_id LEFT JOIN serials u ON u.variant_id=v.variant_id
 WHERE p.tenant_id=sf_tenant() AND EXISTS(SELECT 1 FROM categories cat WHERE cat.tenant_id=p.tenant_id AND cat.category_id=p.category_id AND cat.business_profile_code=sf_visible_business_profile()) AND p.is_active AND v.is_active
 ORDER BY p.product_name,v.variant_id;
 SELECT * FROM replenishment_policies;
 SELECT i.variant_id VariantId,s.supplier_id SupplierId,s.supplier_name SupplierName,i.supplier_sku SupplierCode FROM variant_supplier_identifiers i JOIN suppliers s USING(tenant_id,supplier_id) WHERE s.is_active;
 """;
 async Task<(DateTime Cutoff,List<ReplenishmentRow> Rows)> Read(NpgsqlConnection c,NpgsqlTransaction tx,int days)
 {
  var cutoff=await c.ExecuteScalarAsync<DateTime>("SELECT transaction_timestamp()",transaction:tx);
  using var data=await c.QueryMultipleAsync(Inputs,new{Cutoff=cutoff,Days=days},tx,commandTimeout:30);
  var rows=(await data.ReadAsync<ReplenishmentRow>()).ToList();
  // SQL aliases are explicit because global underscore mapping is not assumed.
  var policies=(await data.ReadAsync()).ToDictionary(x=>(long)x.variant_id,x=>new ReplenishmentPolicy{VariantId=(long)x.variant_id,SupplierId=(int)x.supplier_id,Version=(long)x.version,LeadDays=(int)x.lead_days,SafetyStock=(int)x.safety_stock,CoverageDays=(int)x.coverage_days,Moq=(int)x.moq,OrderMultiple=(int)x.order_multiple});
  var suppliers=(await data.ReadAsync<ReplenishmentSupplier>()).ToDictionary(x=>(x.VariantId,x.SupplierId));
  foreach(var r in rows){if(policies.TryGetValue(r.VariantId,out var policy)&&suppliers.TryGetValue((r.VariantId,policy.SupplierId),out var supplier)){r.Policy=policy;r.SupplierId=supplier.SupplierId;r.SupplierName=supplier.SupplierName;r.SupplierCode=supplier.SupplierCode;}
   ReplenishmentRules.Calculate(r,days);r.Fingerprint=Hash(JsonSerializer.Serialize(new{Day=cutoff.Date,Days=days,Row=r}));}
  return(cutoff,rows);
 }
 static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
 public async Task<ReplenishmentPage> PlanAsync(string? term,string? status,int? supplierId,int days,int page,int pageSize)
 {
  Days(days);if(page<1||page>100000||pageSize is not(10 or 25 or 50 or 100)||term?.Length>150||!string.IsNullOrEmpty(status)&&!Statuses.Contains(status))throw new ValidationException("Invalid planning filters.");
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);await c.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:tx);await Guard(c,tx,"VIEW");
  var snapshot=await Read(c,tx,days);var rows=snapshot.Rows.Where(r=>(string.IsNullOrWhiteSpace(term)||new[]{r.ProductName,r.VariantName,r.Sku}.Any(s=>s.Contains(term.Trim(),StringComparison.OrdinalIgnoreCase)))&&(string.IsNullOrEmpty(status)||r.Status==status)&&(!supplierId.HasValue||r.SupplierId==supplierId)).ToList();
  await tx.CommitAsync();return new(rows.Skip((page-1)*pageSize).Take(pageSize).ToList(),rows.Count,page,pageSize,snapshot.Cutoff,days,new{Review=rows.Count(r=>r.Status!="SUFFICIENT"&&r.Status!="COVERED"),Risk=rows.Count(r=>r.Status=="REORDER_NOW"),RiskUnassessed=rows.Count(r=>r.Policy is null||r.Evidence!="Observed history"||r.Status=="DATA_WARNING"),Suggested=rows.Sum(r=>(long)r.Suggested),Incoming=rows.Sum(r=>r.OpenPo+r.IncomingTransfer)});
 }
 public async Task<long> PrepareAsync(ReplenishmentDraftRequest request)
 {
  Days(request.Days);if(request.OperationId==Guid.Empty||request.SupplierId<=0||request.Lines is null||request.Lines.Count is <1 or >200||request.Lines.Any(l=>l is null||l.Fingerprint is null||l.Fingerprint.Length!=64)||request.Lines.Select(l=>l.VariantId).Distinct().Count()!=request.Lines.Count)throw new ValidationException("Choose distinct planning lines and a retry identity.");
  var payload=JsonSerializer.Serialize(request);var content=JsonSerializer.Serialize(request with{OperationId=Guid.Empty});
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable);await factory.LockBusinessProfileAsync(c,tx);await Guard(c,tx,"PREPARE_PO");
  if(!await c.ExecuteScalarAsync<bool>("SELECT sf_transfer_permission('PURCHASES','CREATE')",transaction:tx))throw new UnauthorizedAccessException("Purchase creation permission required.");
  await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended('replenishment-draft:'||sf_tenant()||':'||sf_location(),0))",transaction:tx);
  var prior=await c.QuerySingleOrDefaultAsync<(long Id,string Input)>("SELECT purchase_id Id,input::text Input FROM replenishment_drafts WHERE operation_id=@OperationId AND actor_id=sf_actor()",request,tx);
  if(prior.Id>0){if(!JsonDocument.Parse(prior.Input).RootElement.ToString().Equals(JsonDocument.Parse(payload).RootElement.ToString())){
    if(!await c.ExecuteScalarAsync<bool>("SELECT input=@payload::jsonb FROM replenishment_drafts WHERE operation_id=@OperationId",new{request.OperationId,payload},tx))throw new ValidationException("Retry payload changed.");}
   await tx.CommitAsync();return prior.Id;}
  var duplicate=await c.ExecuteScalarAsync<long?>("SELECT d.purchase_id FROM replenishment_drafts d JOIN purchases p ON p.tenant_id=d.tenant_id AND p.purchase_id=d.purchase_id WHERE d.actor_id=sf_actor() AND p.status='DRAFT' AND (d.input-'OperationId')=(@content::jsonb-'OperationId') ORDER BY d.created_at LIMIT 1",new{content},tx);
  if(duplicate.HasValue){await tx.CommitAsync();return duplicate.Value;}
  var snapshot=await Read(c,tx,request.Days);var purchase=new CreatePurchaseRequest{SupplierId=request.SupplierId,LocationId=factory.Scope!.LocationId,PurchaseDate=TimeZoneInfo.ConvertTimeFromUtc(snapshot.Cutoff,TimeZoneInfo.FindSystemTimeZoneById((shopTime?.Value??new ShopTimeOptions()).TimeZoneId)).Date,CompleteImmediately=false,InternalReference="Replenishment "+request.OperationId,Notes="Planning draft only. Review prices/tax and quantities before using existing receiving. No supplier confirmation implied."};
  var selected=new List<ReplenishmentRow>();
  foreach(var line in request.Lines){var row=snapshot.Rows.SingleOrDefault(r=>r.VariantId==line.VariantId)??throw new ValidationException("Variant unavailable.");
   if(row.SupplierId!=request.SupplierId||row.Fingerprint!=line.Fingerprint||row.Suggested<=0||row.Warnings.Any(w=>w.StartsWith("Ledger and"))||row.Stock<0||row.DraftPo>0)throw new ValidationException("Plan changed, supplier unavailable, stock discrepancy or existing draft order. Reload and review existing drafts.");
   if(line.Quantity%row.Policy!.OrderMultiple!=0||line.Quantity<row.Policy.Moq||line.PurchasePrice<=0||line.TaxPercentage<0||line.TaxPercentage>100)throw new ValidationException("Confirm positive quantity, current purchase price and tax. Respect MOQ and order multiple.");
   selected.Add(row);purchase.Items.Add(new(){ProductId=row.ProductId,VariantId=row.VariantId,Quantity=line.Quantity,PurchasePrice=line.PurchasePrice,TaxPercentage=line.TaxPercentage,UnitOfMeasure=row.UnitOfMeasure,PackQuantity=1,SupplierProductCode=row.SupplierCode});}
  var id=await PurchaseRepository.CreateInTransactionAsync(c,tx,purchase,factory.Scope.UserId,factory.RequiresExplicitRetailVariant);
  await c.ExecuteAsync("INSERT INTO replenishment_drafts(operation_id,input,snapshot,purchase_id) VALUES(@OperationId,@payload::jsonb,@snapshot::jsonb,@id); INSERT INTO audit_logs(user_id,location_id,module_name,action_name,table_name,record_id,new_values) VALUES(sf_actor(),sf_location(),'Replenishment','PREPARE_PO','purchases',@id,@snapshot::jsonb)",new{request.OperationId,payload,snapshot=JsonSerializer.Serialize(new{snapshot.Cutoff,request.Days,Rows=selected}),id},tx);
  await tx.CommitAsync();return id;
 }
}
