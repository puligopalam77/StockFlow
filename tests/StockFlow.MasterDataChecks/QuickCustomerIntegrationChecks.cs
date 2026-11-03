using Dapper;
using Npgsql;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Customers;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
namespace StockFlow.MasterDataChecks;

internal static class QuickCustomerIntegrationChecks
{
    public static async Task Run()
    {
        const string host="Host=127.0.0.1;Port=55439;Username=postgres;Database=";
        var database="quick_customer_test_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(host+"postgres");await admin.OpenAsync();
        await admin.ExecuteAsync("CREATE DATABASE "+database+" TEMPLATE sku_correction_test");
        try
        {
            await using var setup=new NpgsqlConnection(host+database);await setup.OpenAsync();
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");
            var before=await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text");
            var scope=new BusinessScope{TenantId=1,UserId=1,StoreId=1,LocationId=1,BusinessProfileCode="ELECTRICAL",ProfileDataVisibilityAvailable=true};
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",host+database}}).Build();
            var repository=new CustomerRepository(new DbConnectionFactory(config,scope));var service=new CustomerService(repository);
            CreateCustomerRequest Request(string name,string? phone="9861200011",string? gst="29QUICK1234A1Z5")=>new(){CustomerName=name,MobileNumber=phone,GstNumber=gst};
            async Task<int> Attempt(CreateCustomerRequest request){try{return await service.CreateAsync(request,true);}catch(System.ComponentModel.DataAnnotations.ValidationException){return -1;}}
            var ids=await Task.WhenAll(Attempt(Request("Counter A")),Attempt(Request("Counter B")));
            Check(ids.Count(id=>id>0)==1&&ids.Count(id=>id<0)==1,"concurrent quick-add requests create one customer for the same identifiers");
            var id=ids.Single(id=>id>0);
            Check((await service.GetAllAsync(false,"9861200011")).Count()==1,"duplicate guard commits only one matching row");
            Check(await Attempt(Request("Same phone","9861200011",null))<0,"mobile-only duplicate is rejected");
            Check(await Attempt(Request("Same GST",null,"29QUICK1234A1Z5"))<0,"GST-only duplicate is rejected");
            Check(await service.CreateAsync(Request("Name only",null,null),true)>0,"optional identifiers can be omitted without invented values");
            await repository.DeleteAsync(id);Check(await Attempt(Request("Inactive duplicate"))<0,"inactive customer identifiers remain protected in quick add");
            scope.TenantId=99999;Check(await service.GetByIdAsync(id) is null&&!(await service.GetAllAsync(false,"9861200011")).Any(),"foreign tenant cannot find the customer or identifiers");scope.TenantId=1;
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='BIKE_ACCESSORIES' WHERE tenant_id=1");
            Check(await service.GetByIdAsync(id) is null,"stale shop context cannot read customers");
            scope.BusinessProfileCode="BIKE_ACCESSORIES";
            Check(await service.GetByIdAsync(id) is null&&!(await service.GetAllAsync(false,"9861200011")).Any(),"second shop cannot discover the first shop's customer");
            var secondId=await service.CreateAsync(Request("Independent second shop"),true);
            Check(secondId!=id&&(await service.GetByIdAsync(secondId))?.CustomerName=="Independent second shop","independent shops can register their own contact without cross-shop reuse");
            await setup.ExecuteAsync("UPDATE tenants SET business_profile_code='ELECTRICAL' WHERE tenant_id=1");scope.BusinessProfileCode="ELECTRICAL";
            Check(await service.GetByIdAsync(secondId) is null,"first shop cannot read the second shop customer");
            Check(before==await setup.ExecuteScalarAsync<string>("SELECT (SELECT count(*) FROM sales)::text||':'||(SELECT count(*) FROM inventory_transactions)::text"),"customer registration does not create invoices or inventory movements");
            Console.WriteLine("Quick Customer integration checks passed on a disposable clone only.");
        }
        finally{NpgsqlConnection.ClearAllPools();await admin.ExecuteAsync("DROP DATABASE "+database+" WITH (FORCE)");}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
