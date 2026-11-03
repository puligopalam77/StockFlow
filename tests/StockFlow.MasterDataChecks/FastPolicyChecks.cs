using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Sales;
using StockFlow.Application.DTOs.Security;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using StockFlow.Web.Controllers;
using StockFlow.Web.Security;
using Npgsql;

namespace StockFlow.MasterDataChecks;

internal static class FastPolicyChecks
{
    public static async Task Run(NpgsqlConnection setup, DbConnectionFactory factory, SalesRepository sales, Func<CreateSaleRequest> make)
    {
        static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);}
        static async Task Reject(Func<Task> action,string message){try{await action();}catch(ValidationException){Check(true,message);return;}catch(PostgresException e)when(e.SqlState is "42501" or "23514"){Check(true,message);return;}throw new Exception("FAIL: "+message);}
        PermissionContext Access(BusinessScope scope,bool manage=true)=>new(scope){User=new(){IsActive=true,Permissions=[new(){ModuleCode="SALES",Actions=["VIEW","CREATE","DISCOUNT","PRICE_OVERRIDE","DISCOUNT_APPROVE"]},new(){ModuleCode="COMPANY_SETTINGS",Actions=manage?["VIEW","EDIT"]:["VIEW"]}]}};
        var controller=new BillingController(factory,Access(factory.Scope!));
        CreateSaleRequest Request(decimal rate=5){var r=make();r.AdjustmentReason=null;r.Items[0].AdjustmentReason=null;r.Items[0].DiscountPercent=rate;r.CanApproveDiscount=false;r.AllocateInvoiceDiscount=true;return r;}
        async Task Pay(CreateSaleRequest r){var q=await sales.QuoteAsync(r);r.Payments=[new(){Amount=q.TotalAmount}];}
        var strict=Request();await Pay(strict);await Reject(()=>sales.CreateSaleAsync(strict,1),"existing shops retain mandatory routine reasons before explicit opt-in");
        var denied=new BillingController(factory,Access(factory.Scope!,false));
        Check(await denied.SavePolicy(new(10,true,true,false,true,true)) is ForbidResult,"existing policy management permission protects Fast Billing opt-in");
        Check(await controller.SavePolicy(new(10,true,true,false,true,true)) is OkObjectResult,"authorized owner explicitly enables Fast Billing on existing policy");
        await using var current=factory.CreateConnection();var policy=await BillingPolicyStore.ReadAsync(current);
        Check(await controller.SavePolicy(new(10,Version:policy.Version-1)) is ConflictObjectResult,"stale settings cannot overwrite a newer policy version");
        await controller.SavePolicy(new(10));policy=await BillingPolicyStore.ReadAsync(current);
        Check(policy.FastBillingEnabled&&!policy.RequireRoutineReason&&policy.RequireExceptionalReason&&policy.RequireApprovalAboveLimit,"saved policy distinguishes routine and exceptional requirements");
        foreach(var rate in new[]{0m,5m,10m}){
            var routine=Request(rate);var quote=await sales.QuoteAsync(routine);
            Check(!quote.AdjustmentReview!.RequiresApproval&&!quote.AdjustmentReview.InvoiceReasonRequired&&!quote.AdjustmentReview.LineReasons[0].Discount,$"{rate}% routine discount requires no reason or approval at a strictly-above 10% threshold");
            await Pay(routine);var id=await sales.CreateSaleAsync(routine,1);
            Check(await setup.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM audit_logs WHERE table_name='sales' AND record_id=@id AND action_name='DISCOUNT_POLICY_APPLIED' AND new_values->'Policy'->>'FastBillingEnabled'='true' AND new_values->>'ShopId'='1')",new{id}),"routine checkout records policy version, shop and financial authorization without inventing a reason");
        }
        var invoice=Request(0);invoice.DiscountAmount=5;var iq=await sales.QuoteAsync(invoice);Check(!iq.AdjustmentReview!.InvoiceReasonRequired,"routine invoice discount follows the same no-reason policy");
        var exceptional=Request(15);await Pay(exceptional);await Reject(()=>sales.CreateSaleAsync(exceptional,1),"exceptional discount still requires its configured reason");
        exceptional.Items[0].AdjustmentReason="Synthetic discount exception";
        var service=new StockFlow.Application.Services.SalesService(sales);
        await controller.RequestApproval(exceptional.RequestId,exceptional,service);
        var hash=BillingApproval.Hash(exceptional,policy.Stamp,await sales.QuoteAsync(exceptional));
        Check(await controller.Approve(exceptional.RequestId,new(hash)) is ConflictObjectResult,"cashier cannot approve their own approval request");
        await Pay(exceptional);await Reject(()=>sales.CreateSaleAsync(exceptional,1),"pending exception cannot finalize");
        var managerScope=new BusinessScope{TenantId=1,UserId=2,StoreId=1,LocationId=1,BusinessProfileCode=factory.Scope!.BusinessProfileCode,ProfileDataVisibilityAvailable=true,CatalogRegistrationEnhancementsAvailable=true};
        // Same connection settings, authenticated as the existing second manager.
        var managerConfig=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",setup.ConnectionString}}).Build();
        var managerFactory=new DbConnectionFactory(managerConfig,managerScope);
        await setup.ExecuteAsync("INSERT INTO user_action_permissions(tenant_id,user_id,module_action_id,is_allowed) SELECT 1,2,a.module_action_id,true FROM module_actions a JOIN modules m USING(module_id) WHERE m.module_code='SALES' AND a.action_code IN('VIEW','DISCOUNT_APPROVE') ON CONFLICT(user_id,module_action_id) DO UPDATE SET is_allowed=true");
        var manager=new BillingController(managerFactory,Access(managerScope));
        var unprivileged=new PermissionContext(managerScope){User=new(){IsActive=true,Permissions=[new(){ModuleCode="SALES",Actions=["VIEW","DISCOUNT"]}]}};
        Check(await new BillingController(managerFactory,unprivileged).Approve(exceptional.RequestId,new(hash)) is ForbidResult,"unprivileged approver is rejected on backend");
        Check(await manager.Approve(exceptional.RequestId,new(hash,true)) is NoContentResult,"existing approval workflow records manager rejection");
        Check((await sales.QuoteAsync(exceptional)).AdjustmentReview!.ApprovalStatus=="REJECTED","quote reports rejected approval distinctly");
        await Reject(()=>sales.CreateSaleAsync(exceptional,1),"rejected exception cannot finalize");
        await controller.RequestApproval(exceptional.RequestId,exceptional,service);
        Check(await manager.Approve(exceptional.RequestId,new(hash)) is NoContentResult,"different authorized manager approves exact requested amounts");
        exceptional.Items[0].SellingPrice=95;
        Check((await sales.QuoteAsync(exceptional)).AdjustmentReview!.ApprovalStatus=="STALE","selling price edit invalidates approval");exceptional.Items[0].SellingPrice=100;
        exceptional.Items[0].DiscountPercent=16;
        Check((await sales.QuoteAsync(exceptional)).AdjustmentReview!.ApprovalStatus=="STALE","discount edit invalidates approval");exceptional.Items[0].DiscountPercent=15;
        exceptional.Items[0].Quantity++;
        Check((await sales.QuoteAsync(exceptional)).AdjustmentReview!.ApprovalStatus=="STALE","quantity edit invalidates approval");exceptional.Items[0].Quantity--;
        await controller.SavePolicy(new(10,true,true,false,true,true));
        Check((await sales.QuoteAsync(exceptional)).AdjustmentReview!.ApprovalStatus=="STALE","policy version change invalidates previously approved amounts");
        await controller.RequestApproval(exceptional.RequestId,exceptional,service);
        policy=await BillingPolicyStore.ReadAsync(current);hash=BillingApproval.Hash(exceptional,policy.Stamp,await sales.QuoteAsync(exceptional));
        await manager.Approve(exceptional.RequestId,new(hash));await Pay(exceptional);await sales.CreateSaleAsync(exceptional,1);
        var combined=Request(7);combined.DiscountAmount=9;
        Check((await sales.QuoteAsync(combined)).AdjustmentReview!.RequiresApproval,"combined item and invoice reductions cannot bypass the threshold");
        var unauthorized=Request();unauthorized.CanDiscount=false;await Pay(unauthorized);await Reject(()=>sales.CreateSaleAsync(unauthorized,1),"no-reason routine discounts still require discount permission");
        var secondStore=await setup.ExecuteScalarAsync<long>("INSERT INTO stores(tenant_id,store_code,store_name) VALUES(1,'POLICY-TEST','Policy isolation test') RETURNING store_id");
        factory.Scope.StoreId=secondStore;
        await using(var other=factory.CreateConnection())Check(!(await BillingPolicyStore.ReadAsync(other)).FastBillingEnabled,"another shop retains strict inherited policy");
        factory.Scope.StoreId=1;
        await setup.ExecuteAsync("INSERT INTO billing_policy(tenant_id,store_id,discount_limit_percent) VALUES(1,@id,3)",new{id=secondStore});
        await using(var isolated=factory.CreateConnection())Check(await isolated.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_policy WHERE store_id=@id",new{id=secondStore})==0,"runtime cannot read another shop policy row");
        await setup.ExecuteAsync("DELETE FROM billing_policy WHERE tenant_id=1 AND store_id=1");
        Console.WriteLine("Fast Billing policy and existing approval workflow checks passed.");
    }
}


