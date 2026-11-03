using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Infrastructure.Data;
namespace StockFlow.Web.Controllers;
[Authorize,ApiController,Route("api/products/vehicles")]
public sealed class ProductVehiclesController(DbConnectionFactory factory):ControllerBase
{
 [HttpGet]
 public async Task<IActionResult> Get(string? term=null)
 {
  await using var connection=factory.CreateConnection();
  if(!await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.vehicle_models') IS NOT NULL"))return Ok(new{available=false,items=Array.Empty<object>()});
  term=term?.Trim();if(term?.Length>100)return BadRequest(new{message="Search vehicles with at most 100 characters."});
  var rows=await connection.QueryAsync("SELECT vehicle_id AS \"vehicleId\",manufacturer,model,variant FROM vehicle_models WHERE @Term IS NULL OR concat_ws(' ',manufacturer,model,variant) ILIKE @Term ORDER BY manufacturer,model,variant,vehicle_id LIMIT 50",new{Term=string.IsNullOrWhiteSpace(term)?null:"%"+term+"%"});
  return Ok(new{available=true,items=rows});
 }
}
