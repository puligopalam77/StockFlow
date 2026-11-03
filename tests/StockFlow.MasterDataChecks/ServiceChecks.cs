using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Service;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
namespace StockFlow.MasterDataChecks;

internal static class ServiceChecks
{
    public static async Task Run(Factory factory)
    {
        using var anonymous=factory.CreateClient();
        Check((int)(await anonymous.GetAsync("/api/service")).StatusCode==401,"anonymous service denied");
        foreach(var role in new[]{"Admin","Sales","Service"})
        {
            using var client=factory.CreateClient();
            client.DefaultRequestHeaders.Add("Test-Role",role); client.DefaultRequestHeaders.Add("Test-UserId","1");
            var token=await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token!["requestToken"]!.GetValue<string>());
            foreach(var url in new[]{"/Service","/Service/Pending","/Service/Details/1","/api/service","/api/service/pending",
                "/api/service?status=DELIVERED","/api/service/1","/api/service/1/history","/api/service/1/parts","/api/service/technicians","/api/service/units/1"})
                Check((await client.GetAsync(url)).IsSuccessStatusCode,role+" reads "+url);
            Check((int)(await client.GetAsync("/Service/Create")).StatusCode==(role=="Sales"?403:200),role+" create page permission");
            Check((int)(await client.GetAsync("/api/service/999")).StatusCode==404,"missing service");
            foreach(var url in new[]{"/api/service?status=BOGUS","/api/service?offset=-1","/api/service?from=2026-10-01&to=2026-09-01","/api/service/pending?status=DELIVERED","/api/service/1/parts?offset=-1"})
                Check((int)(await client.GetAsync(url)).StatusCode==400,"invalid service filters");
            var create=new CreateServiceRequest{RequestId=Guid.NewGuid(),ProductId=1,CustomerId=1,Complaint="Not charging"};
            Check((int)(await client.PostAsJsonAsync("/api/service",create)).StatusCode==(role=="Sales"?403:201),role+" create permission");
            Check((int)(await client.PutAsJsonAsync("/api/service/1",new UpdateServiceRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,Diagnosis="Test",ServiceCharge=25})).StatusCode==(role=="Sales"?403:204),role+" edit permission");
            Check((int)(await client.PutAsJsonAsync("/api/service/1/status",new UpdateServiceStatusRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,Status="INSPECTION",Remarks="Inspect"})).StatusCode==(role=="Sales"?403:204),role+" status permission");
            Check((int)(await client.PostAsJsonAsync("/api/service/1/parts",new AddServicePartRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,ProductId=2,Quantity=2,UnitPrice=10})).StatusCode==(role=="Sales"?403:200),role+" parts permission");
            var html=await client.GetStringAsync("/Service/Details/1");
            Check(html.Contains("details-form")==(role!="Sales"),"read-only service details for Sales");
            if(role!="Sales")
            {
                Check((int)(await client.PostAsJsonAsync("/api/service",new CreateServiceRequest{RequestId=Guid.NewGuid(),ProductId=1,CustomerId=1,Complaint=" "})).StatusCode==400,"empty complaint rejected");
                Check((int)(await client.PutAsJsonAsync("/api/service/1/status",new UpdateServiceStatusRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,Status="BOGUS",Remarks="Invalid"})).StatusCode==400,"invalid status rejected");
                client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
                Check((int)(await client.PostAsJsonAsync("/api/service",create)).StatusCode==400,"service CSRF required");
            }
        }
        foreach(var (before,after) in new[]{("RECEIVED","INSPECTION"),("INSPECTION","WAITING_FOR_PART"),("WAITING_FOR_PART","REPAIRING"),("REPAIRING","READY"),("READY","DELIVERED")})
            ServiceRules.Transition(before,after);
        foreach(var (before,after) in new[]{("RECEIVED","DELIVERED"),("DELIVERED","REPAIRING"),("CANCELLED","INSPECTION"),("READY","READY")})
            Reject(()=>ServiceRules.Transition(before,after));
        Reject(()=>ServiceRules.Validate(new AddServicePartRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,ProductId=2,Quantity=0}));
        Reject(()=>ServiceRules.Validate(new AddServicePartRequest{RequestId=Guid.NewGuid(),ExpectedVersion=1,ProductId=2,Quantity=2,ProductUnitId=1}));
        Reject(()=>ServiceRules.Money(-1));
        Reject(()=>ServiceRules.Money(0.001m));
        Reject(()=>ServiceRules.Validate(new UpdateServiceRequest{RequestId=Guid.NewGuid(),ExpectedVersion=0}));
        Console.WriteLine("PASS: service API/page roles, CSRF, filters, amounts, serialized part rules and status transitions. Fake repository; no database writes.");
    }
    static void Reject(Action action) { try { action(); } catch(ValidationException) { return; } throw new Exception("Expected service validation rejection."); }
    static void Check(bool ok,string label) { if(!ok) throw new Exception("FAIL: "+label); }
}
internal sealed class FakeServiceRepository : IServiceRepository
{
    public Task<long> CreateAsync(CreateServiceRequest request,int userId)=>Task.FromResult(1L);
    public Task<ServiceRequestResponse?> GetByIdAsync(long id)=>Task.FromResult<ServiceRequestResponse?>(id==1?new(){ServiceRequestId=1,ServiceNumber="SRV-TEST-1",Status="RECEIVED",Version=1}:null);
    public Task<IEnumerable<ServiceRequestResponse>> GetAllAsync(ServiceSearch search)=>Task.FromResult<IEnumerable<ServiceRequestResponse>>([]);
    public Task UpdateAsync(long id,UpdateServiceRequest request,int userId)=>Task.CompletedTask;
    public Task UpdateStatusAsync(long id,UpdateServiceStatusRequest request,int userId)=>Task.CompletedTask;
    public Task<long> AddPartAsync(long id,AddServicePartRequest request,int userId)=>Task.FromResult(1L);
    public Task<IEnumerable<ServiceHistory>> GetHistoryAsync(long id,int offset)=>Task.FromResult<IEnumerable<ServiceHistory>>([]);
    public Task<IEnumerable<ServicePart>> GetPartsAsync(long id,int offset)=>Task.FromResult<IEnumerable<ServicePart>>([]);
    public Task<IEnumerable<ServiceTechnician>> GetTechniciansAsync()=>Task.FromResult<IEnumerable<ServiceTechnician>>([new(){UserId=1,FullName="Technician"}]);
    public Task<ServiceUnitContext?> GetUnitContextAsync(long unitId)=>Task.FromResult<ServiceUnitContext?>(new(){ProductUnitId=unitId,ProductId=1,CustomerId=1,UnitStatus="SOLD"});
}
