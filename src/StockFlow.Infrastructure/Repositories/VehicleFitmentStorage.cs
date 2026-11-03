using Dapper;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StockFlow.Application.DTOs.Products;
namespace StockFlow.Infrastructure.Repositories;
internal static class VehicleFitmentStorage
{
 internal static Task<bool> AvailableAsync(NpgsqlConnection c,NpgsqlTransaction? t=null)=>c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.product_vehicle_fitments') IS NOT NULL",transaction:t);
 internal static async Task SaveAsync(NpgsqlConnection c,NpgsqlTransaction t,int productId,long? variantId,List<VehicleFitment>? entries)
 {
  if(entries is null)return;
  if(!await AvailableAsync(c,t)){if(entries.Count>0)throw new ValidationException("Apply migration 066 before saving vehicle master links.");return;}
  if(entries.Count>30)throw new ValidationException("Use up to 30 verified vehicle entries.");
  await c.ExecuteAsync("DELETE FROM product_vehicle_fitments WHERE product_id=@productId AND variant_id IS NOT DISTINCT FROM @variantId",new{productId,variantId},t);
  var unique=new HashSet<string>();
  foreach(var entry in entries)
  {
   Validator.ValidateObject(entry,new ValidationContext(entry),true);entry.Manufacturer=entry.Manufacturer.Trim();entry.Model=entry.Model.Trim();entry.Variant=entry.Variant?.Trim()??"";entry.Notes=entry.Notes?.Trim();
   if(entry.YearFrom>entry.YearTo || entry.Manufacturer.Any(char.IsControl)||entry.Model.Any(char.IsControl)||entry.Variant.Any(char.IsControl))throw new ValidationException("Use a valid verified vehicle and year range.");
   var modelId=entry.VehicleId;
   if(modelId.HasValue){if(!await c.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM vehicle_models WHERE vehicle_id=@modelId AND lower(btrim(manufacturer))=lower(@Manufacturer) AND lower(btrim(model))=lower(@Model) AND lower(btrim(variant))=lower(@Variant))",new{modelId,entry.Manufacturer,entry.Model,entry.Variant},t))throw new ValidationException("Choose a vehicle from this shop, preserving its registered identity.");}
   else
   {
    await c.ExecuteAsync("INSERT INTO vehicle_models(manufacturer,model,variant) VALUES(@Manufacturer,@Model,@Variant) ON CONFLICT DO NOTHING",entry,t);
    modelId=await c.ExecuteScalarAsync<long>("SELECT vehicle_id FROM vehicle_models WHERE lower(btrim(manufacturer))=lower(@Manufacturer) AND lower(btrim(model))=lower(@Model) AND lower(btrim(variant))=lower(@Variant)",entry,t);
   }
   if(!unique.Add($"{modelId}:{entry.YearFrom}:{entry.YearTo}"))throw new ValidationException("This vehicle/year fitment is already listed.");
   await c.ExecuteAsync("INSERT INTO product_vehicle_fitments(product_id,variant_id,vehicle_id,year_from,year_to,notes) VALUES(@productId,@variantId,@modelId,@YearFrom,@YearTo,@Notes)",new{productId,variantId,modelId,entry.YearFrom,entry.YearTo,entry.Notes},t);
  }
 }
 internal static async Task<List<(int ProductId,long? VariantId,VehicleFitment Fitment)>> LoadAsync(NpgsqlConnection c,int[] ids)
 {
  if(ids.Length==0||!await AvailableAsync(c))return [];
  var rows=await c.QueryAsync<(int ProductId,long? VariantId,string Json)>("SELECT f.product_id,f.variant_id,jsonb_build_object('vehicleId',v.vehicle_id,'manufacturer',v.manufacturer,'model',v.model,'variant',v.variant,'yearFrom',f.year_from,'yearTo',f.year_to,'notes',f.notes)::text FROM product_vehicle_fitments f JOIN vehicle_models v USING(tenant_id,vehicle_id) WHERE f.product_id=ANY(@ids) ORDER BY f.fitment_id",new{ids});
  return rows.Select(r=>(r.ProductId,r.VariantId,JsonSerializer.Deserialize<VehicleFitment>(r.Json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!)).ToList();
 }
}
