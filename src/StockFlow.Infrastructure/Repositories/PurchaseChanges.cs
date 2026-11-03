using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;
namespace StockFlow.Infrastructure.Repositories;

public partial class PurchaseRepository
{
 public async Task<IReadOnlyList<PurchaseChange>> HistoryAsync(long id)
 {
  using var c=factory.CreateConnection();
  return (await c.QueryAsync<PurchaseChange>("""
   SELECT h.change_id ChangeId,h.change_type ChangeType,h.reason Reason,h.changed_at ChangedAt,
    coalesce(u.full_name,u.username,h.changed_by::text) ChangedBy,h.amount_delta AmountDelta,h.adjustment_id AdjustmentId,
    h.old_values::text OldJson,h.new_values::text NewJson
   FROM purchase_changes h LEFT JOIN users u ON u.tenant_id=h.tenant_id AND u.user_id=h.changed_by
   WHERE h.purchase_id=@id ORDER BY h.change_id DESC LIMIT 100
   """,new{id})).ToList();
 }
 internal static Task<string> ChangeSnapshot(NpgsqlConnection c,NpgsqlTransaction t,long id) => c.QuerySingleAsync<string>("""
  SELECT jsonb_build_object('details',to_jsonb(p)-'invoice_attachment_data','items',
    coalesce((SELECT jsonb_agg(to_jsonb(i) ORDER BY i.purchase_item_id) FROM purchase_items i WHERE i.purchase_id=p.purchase_id),'[]'::jsonb))::text
  FROM purchases p WHERE p.purchase_id=@id
  """,new{id},t);
 internal static Task<int> WriteChange(NpgsqlConnection c,NpgsqlTransaction t,long id,Guid requestId,string kind,string reason,
   string before,string after,string payload,int userId,decimal delta=0,long? adjustment=null,PurchaseAttachment? oldFile=null) => c.ExecuteAsync("""
  INSERT INTO purchase_changes(purchase_id,request_id,change_type,reason,old_values,new_values,request_payload,changed_by,amount_delta,adjustment_id,
   old_attachment_name,old_attachment_content_type,old_attachment_data)
  VALUES(@id,@requestId,@kind,@reason,@before::jsonb,@after::jsonb,@payload::jsonb,@userId,@delta,@adjustment,@FileName,@ContentType,@Data)
  """,new{id,requestId,kind,reason,before,after,payload,userId,delta,adjustment,FileName=oldFile?.FileName,ContentType=oldFile?.ContentType,Data=oldFile?.Data},t);
 private static Task<PurchaseAttachment?> OldFile(NpgsqlConnection c,NpgsqlTransaction t,long id) => c.QuerySingleOrDefaultAsync<PurchaseAttachment>(
  "SELECT invoice_attachment_name FileName,invoice_attachment_content_type ContentType,invoice_attachment_data Data FROM purchases WHERE purchase_id=@id AND invoice_attachment_data IS NOT NULL",new{id},t);
 internal static void Version(long actual,long? expected)
 {
  if(expected is null || actual!=expected)throw new ValidationException("This purchase changed since you opened it. Reload and review the latest details.");
 }
 public async Task<long> ChangeAsync(long id,PurchaseChangeRequest input,int userId)
 {
  factory.RequireLocation(input.LocationId);
  Validator.ValidateObject(input,new ValidationContext(input),true);
  if(input.RequestId==Guid.Empty||userId<=0||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Trim().Length<3)throw new ValidationException("Enter a reason for this change.");
  foreach(var line in input.Items){if(line is null)throw new ValidationException("Invalid purchase line.");Validator.ValidateObject(line,new ValidationContext(line),true);}
  var payload=JsonSerializer.Serialize(input);
  await using var c=factory.CreateConnection();await c.OpenAsync();await using var t=await c.BeginTransactionAsync();await factory.LockBusinessProfileAsync(c,t);
  await c.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",new{key="StockFlow.PurchaseChange:"+input.RequestId},t);
  var replay=await c.QuerySingleOrDefaultAsync<ChangeReplay>("SELECT change_id Id,changed_by=@userId AND request_payload=@payload::jsonb AND purchase_id=@id Matches FROM purchase_changes WHERE request_id=@RequestId",new{input.RequestId,userId,payload,id},t);
  if(replay is not null){if(!replay.Matches)throw new ValidationException("This request ID was already used for another correction.");await t.CommitAsync();return replay.Id;}
  var status=await LockPurchaseAsync(c,t,id)??throw new ValidationException("Purchase not found at this location.");
  if(status=="CANCELLED")throw new ValidationException("Cancelled purchases are view only.");
  if(status is not ("DRAFT" or "PARTIALLY_RECEIVED" or "COMPLETED"))throw new ValidationException("This purchase status cannot be edited.");
  var purchase=(await ReadAsync(c,t,id))!;Version(purchase.EditVersion,input.ExpectedVersion);
  var before=await ChangeSnapshot(c,t,id);var oldFile=await OldFile(c,t,id);
  var request=new CreatePurchaseRequest{SupplierId=purchase.SupplierId,PurchaseDate=purchase.PurchaseDate,
   SupplierInvoiceNumber=input.SupplierInvoiceNumber,SupplierInvoiceDate=input.SupplierInvoiceDate,Notes=input.Notes,
   InvoiceAttachmentName=input.InvoiceAttachmentName,InvoiceAttachmentContentType=input.InvoiceAttachmentContentType,InvoiceAttachmentData=input.InvoiceAttachmentData,
   DiscountAmount=purchase.DiscountAmount,FreightAmount=purchase.FreightAmount,OtherLandedCost=purchase.OtherLandedCost,
   Items=purchase.Items.Select(i=>new CreatePurchaseItemRequest{ProductId=i.ProductId,VariantId=i.VariantId,Quantity=i.Quantity,PurchasePrice=i.PurchasePrice,DiscountAmount=i.DiscountAmount,TaxPercentage=i.TaxPercentage}).ToList()};
  decimal delta=0;
  if(input.Kind!="DETAILS")
  {
   if(input.Items.Count!=purchase.Items.Count||input.Items.Select(i=>i.PurchaseItemId).Distinct().Count()!=input.Items.Count||input.Items.Any(i=>purchase.Items.All(p=>p.PurchaseItemId!=i.PurchaseItemId)))throw new ValidationException("Keep the existing purchase items. Reload before correcting them.");
   if(input.Kind=="ORDER"&&status=="COMPLETED")throw new ValidationException("Received quantities cannot be overwritten. Use a stock correction.");
   foreach(var p in purchase.Items.OrderBy(i=>i.ProductId).DistinctBy(i=>i.ProductId))await c.ExecuteAsync("SELECT product_id FROM products WHERE product_id=@ProductId FOR UPDATE",p,t);
   if(input.Kind=="FINANCIAL")
   {
    if(await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM purchase_returns WHERE purchase_id=@id)",new{id},t))throw new ValidationException("This purchase has supplier returns. Resolve those financial documents before changing its valuation.");
    if(await c.ExecuteScalarAsync<bool>("""
     SELECT EXISTS(SELECT 1 FROM inventory_transactions m JOIN purchase_items i ON i.variant_id=m.variant_id
      WHERE i.purchase_id=@id AND m.created_at>=@CreatedAt AND m.transaction_type IN('SALE','SUPPLIER_RETURN','SERVICE_OUT','WARRANTY_REPLACEMENT','TRANSFER_OUT','TRANSFER_LOSS','ADJUSTMENT_OUT','DAMAGED'))
     """,new{id,purchase.CreatedAt},t))throw new ValidationException("Stock from these variants has moved out. A valuation correction requires an accounting review; historical sales and stock costs will not be overwritten.");
    request.DiscountAmount=input.DiscountAmount;request.FreightAmount=input.FreightAmount;request.OtherLandedCost=input.OtherLandedCost;
   }
   for(int n=0;n<purchase.Items.Count;n++)
   {
    var old=purchase.Items[n];var line=input.Items.Single(i=>i.PurchaseItemId==old.PurchaseItemId);var next=request.Items[n];
    if(input.Kind=="ORDER")
    {
     if(line.Quantity<old.ReceivedQuantity+old.DamagedQuantity)throw new ValidationException("Ordered quantity cannot be below already received and damaged quantity.");
     if(line.PurchasePrice!=old.PurchasePrice||line.DiscountAmount!=old.DiscountAmount||line.TaxPercentage!=old.TaxPercentage)throw new ValidationException("Use Correct Purchase for price, discount or tax changes.");
     next.Quantity=line.Quantity;
    }
    else
    {
     if(line.Quantity!=old.Quantity)throw new ValidationException("Financial corrections cannot change ordered or received quantities.");
     next.PurchasePrice=line.PurchasePrice;next.DiscountAmount=line.DiscountAmount;next.TaxPercentage=line.TaxPercentage;
    }
   }
  }
  else if(input.Items.Count!=0)throw new ValidationException("Detail edits cannot include item changes.");
  var totals=PurchaseRules.ValidateAndCalculate(request);delta=input.Kind=="DETAILS"?0:totals.Total-purchase.TotalAmount;
  var paid=await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(amount),0) FROM supplier_payments WHERE purchase_id=@id",new{id},t);
  var returned=await c.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(return_total),0) FROM purchase_returns WHERE purchase_id=@id",new{id},t);
  if(input.Kind!="DETAILS"&&totals.Total<paid+returned)throw new ValidationException("The corrected total cannot be below recorded payments and returns. Reconcile the supplier credit first.");
  if(input.Kind!="DETAILS")for(int n=0;n<purchase.Items.Count;n++)
  {
   var item=request.Items[n];var lineTotal=PurchaseRules.Money((item.Quantity*item.PurchasePrice-item.DiscountAmount)*(1+item.TaxPercentage/100));
   await c.ExecuteAsync("UPDATE purchase_items SET quantity=@Quantity,purchase_price=@PurchasePrice,discount_amount=@DiscountAmount,tax_percentage=@TaxPercentage,line_total=@lineTotal WHERE purchase_item_id=@itemId",new{item.Quantity,item.PurchasePrice,item.DiscountAmount,item.TaxPercentage,lineTotal,itemId=purchase.Items[n].PurchaseItemId},t);
  }
  if(input.Kind=="DETAILS")
  {
   await c.ExecuteAsync("""
    UPDATE purchases SET supplier_invoice_number=@SupplierInvoiceNumber,supplier_invoice_date=@SupplierInvoiceDate,notes=@Notes,internal_reference=@reference,
    invoice_attachment_name=coalesce(@InvoiceAttachmentName,invoice_attachment_name),invoice_attachment_content_type=coalesce(@InvoiceAttachmentContentType,invoice_attachment_content_type),invoice_attachment_data=coalesce(@InvoiceAttachmentData,invoice_attachment_data) WHERE purchase_id=@id
    """,new{id,request.SupplierInvoiceNumber,request.SupplierInvoiceDate,request.Notes,reference=input.InternalReference?.Trim(),request.InvoiceAttachmentName,request.InvoiceAttachmentContentType,request.InvoiceAttachmentData},t);
  }
  else await c.ExecuteAsync("""
   UPDATE purchases SET subtotal=@Subtotal,tax_amount=@Tax,total_amount=@Total,discount_amount=@DiscountAmount,freight_amount=@FreightAmount,other_landed_cost=@OtherLandedCost,
    status=CASE WHEN status='DRAFT' THEN 'DRAFT' WHEN EXISTS(SELECT 1 FROM purchase_items WHERE purchase_id=@id AND quantity>received_quantity+damaged_quantity) THEN 'PARTIALLY_RECEIVED' ELSE 'COMPLETED' END WHERE purchase_id=@id
   """,new{id,totals.Subtotal,totals.Tax,totals.Total,request.DiscountAmount,request.FreightAmount,request.OtherLandedCost},t);
  var after=await ChangeSnapshot(c,t,id);
  using(var a=JsonDocument.Parse(before))using(var b=JsonDocument.Parse(after))
  {
   // Ignore the optimistic-lock version when detecting no-op detail requests.
   if(input.Kind=="DETAILS"&&purchase.SupplierInvoiceNumber==request.SupplierInvoiceNumber&&purchase.SupplierInvoiceDate==request.SupplierInvoiceDate&&purchase.Notes==request.Notes&&purchase.InternalReference==input.InternalReference?.Trim()&&request.InvoiceAttachmentData is null)throw new ValidationException("No changes to save.");
  }
  await WriteChange(c,t,id,input.RequestId,input.Kind,input.Reason.Trim(),before,after,payload,userId,delta,oldFile:oldFile);
  var changeId=await c.ExecuteScalarAsync<long>("SELECT change_id FROM purchase_changes WHERE request_id=@RequestId",input,t);
  await t.CommitAsync();return changeId;
 }
 private sealed class ChangeReplay{public long Id{get;set;}public bool Matches{get;set;}}
}
