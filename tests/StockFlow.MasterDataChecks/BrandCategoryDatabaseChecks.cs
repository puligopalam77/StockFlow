using Dapper;
using Npgsql;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;
internal static class BrandCategoryDatabaseChecks
{
 public static async Task Run(NpgsqlConnection admin,DbConnectionFactory factory)
 {
  Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM brand_categories WHERE tenant_id=1 AND brand_id=1 AND category_id=1")==1,"migration backfills existing product brand/category");
  var second=await admin.ExecuteScalarAsync<int>("INSERT INTO categories(tenant_id,category_name) VALUES(1,'Battery') RETURNING category_id");
  var foreign=await admin.ExecuteScalarAsync<int>("SELECT category_id FROM categories WHERE tenant_id=2 LIMIT 1");
  var repository=new BrandRepository(factory);
  var brand=new Brand{BrandName="Shared test brand",CategoryIds=[1,second],IsActive=true};
  brand.BrandId=await repository.CreateAsync(brand);
  Check((await repository.GetByIdAsync(brand.BrandId))!.CategoryIds.SequenceEqual(new[]{1,second}.Order()),"brand repository saves and reads multiple categories");
  brand.BrandName="Renamed shared brand";brand.CategoryIds=[second];await repository.UpdateAsync(brand);
  Check((await repository.GetByIdAsync(brand.BrandId))!.CategoryIds.SequenceEqual(new[]{second}),"brand update atomically replaces assignments");
  var invalid=new Brand{BrandName="Foreign category brand",CategoryIds=[foreign],IsActive=true};
  await Reject(()=>repository.CreateAsync(invalid),"cross-tenant category cannot be assigned");
  Check(!(await repository.GetAllAsync()).Any(b=>b.BrandName==invalid.BrandName),"failed category assignment rolls back brand creation");
  await using var scoped=factory.CreateConnection();await scoped.OpenAsync();
  Check(await scoped.ExecuteScalarAsync<int>("SELECT count(*) FROM brand_categories WHERE tenant_id=2")==0,"brand/category assignments obey tenant RLS");
  await Reject(()=>scoped.ExecuteAsync("DELETE FROM brand_categories WHERE brand_id=1 AND category_id=1"),"database prevents removing assignment used by active product");
  await Reject(()=>scoped.ExecuteAsync("UPDATE products SET brand_id=@BrandId WHERE product_id=1",brand),"database rejects mismatched category/brand directly");
  brand.CategoryIds=[second,1];await repository.UpdateAsync(brand);
  await scoped.ExecuteAsync("UPDATE products SET brand_id=@BrandId WHERE product_id=1",brand);
  Check(await scoped.ExecuteScalarAsync<int>("SELECT brand_id FROM products WHERE product_id=1")==brand.BrandId,"database accepts matching category/brand");
  Console.WriteLine("Brand/category database checks passed against all migrations in an isolated database.");
 }
 static async Task Reject(Func<Task> action,string name){try{await action();}catch(PostgresException){Console.WriteLine("PASS: "+name);return;}throw new Exception("FAIL: "+name);}
 static void Check(bool passed,string name){if(!passed)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
}
