using Microsoft.AspNetCore.Http;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
namespace StockFlow.MasterDataChecks;
sealed class FakeSecurityRepository(IHttpContextAccessor context):ISecurityRepository
{
    public Task<UserAccess?> GetAccessAsync(int id)
    {
        var headers=context.HttpContext!.Request.Headers;var role=headers["Test-Role"].ToString();bool admin=role=="Admin",sales=role=="Sales",service=role=="Service";
        var codes=new[]{"DASHBOARD","SCAN","PRODUCTS","CATEGORIES","BRANDS","SUPPLIERS","CUSTOMERS","PURCHASES","STOCK_INFLOW","STOCK_OUTFLOW","INVENTORY","SALES","INVOICES","WARRANTY_LOOKUP","WARRANTY_CLAIMS","EXPIRING_WARRANTIES","SERVICE","PENDING_SERVICES","REPORTS","SEARCH","COMPANY_SETTINGS","USER_MANAGEMENT","SECURITY","AUDIT_LOGS","STORES","STOCK_TRANSFER","TRANSFER_DISPATCH","TRANSFER_RECEIVE","TRANSFER_CANCEL","PRODUCT_VARIANTS","BARCODES","STOCK_ADJUSTMENT","ROLE_MANAGEMENT","GST_SETTINGS","GST_REPORTS","CRM"};
        var perms=codes.Select((code,i)=>new ModulePermission{ModuleId=i+1,ModuleCode=code,ModuleName=code,SupportedActions=StockFlow.Application.Common.PermissionCatalog.Modules.GetValueOrDefault(code,[]),IsActive=true,CanView=admin || (sales||service)&&!new[]{"SUPPLIERS","PURCHASES","COMPANY_SETTINGS","USER_MANAGEMENT","SECURITY","AUDIT_LOGS","GST_SETTINGS","GST_REPORTS","CRM"}.Contains(code)&&!(service&&code=="SALES"),CanCreate=admin||sales&&new[]{"CUSTOMERS","SALES"}.Contains(code)||service&&new[]{"SERVICE","WARRANTY_LOOKUP","WARRANTY_CLAIMS"}.Contains(code),CanEdit=admin||sales&&code=="CUSTOMERS"||service&&new[]{"SERVICE","WARRANTY_LOOKUP","WARRANTY_CLAIMS"}.Contains(code),CanDelete=admin,CanApprove=admin||service&&new[]{"SERVICE","WARRANTY_LOOKUP","WARRANTY_CLAIMS"}.Contains(code),CanExport=admin||new[]{"INVOICES","REPORTS"}.Contains(code)}).ToList();
        perms.Add(new ModulePermission{ModuleId=1000,ModuleCode="QUOTATIONS",ModuleName="Quotations",SupportedActions=StockFlow.Application.Common.PermissionCatalog.Modules["QUOTATIONS"],IsActive=true,CanView=admin,CanCreate=admin,CanEdit=admin,CanExport=admin});
        perms.Add(new ModulePermission{ModuleId=1001,ModuleCode="EWAY_BILLS",ModuleName="E-Way Bills",SupportedActions=StockFlow.Application.Common.PermissionCatalog.Modules["EWAY_BILLS"],IsActive=true,CanView=admin,CanCreate=admin,CanEdit=admin,CanExport=admin});
        perms.Add(new ModulePermission{ModuleId=1002,ModuleCode="EXPENSES",ModuleName="Expenses",SupportedActions=StockFlow.Application.Common.PermissionCatalog.Modules["EXPENSES"],IsActive=true,CanView=admin,CanCreate=admin,CanEdit=admin,CanApprove=admin,CanExport=admin});
        perms.Add(new ModulePermission{ModuleId=1003,ModuleCode="REPLENISHMENT",ModuleName="Replenishment",SupportedActions=StockFlow.Application.Common.PermissionCatalog.Modules["REPLENISHMENT"],IsActive=true,CanView=admin,CanCreate=admin,CanEdit=admin,CanExport=admin});
        foreach(var p in perms){if(headers["Test-Deny"].ToString().Split(',').Contains(p.ModuleCode))p.CanView=false;if(headers["Test-Grant-Create"]==p.ModuleCode){p.CanView=true;p.CanCreate=true;}if(headers["Test-Deny-Approve"]==p.ModuleCode)p.CanApprove=false;}
        foreach(var p in perms)
        {
            p.Actions=p.SupportedActions.Where(a=>p.CanView && (a switch {"VIEW" or "SCAN" or "PRINT" or "REPRINT"=>true,"CREATE"=>p.CanCreate,"DEACTIVATE"=>p.CanDelete,"EXPORT"=>p.CanExport,"APPROVE" or "RECEIVE" or "DISPATCH" or "CANCEL" or "ADJUST" or "DELIVER" or "RESOLVE"=>p.CanApprove,_=>p.CanEdit})).ToArray();
            var deniedActions=headers["Test-Deny-Action"].ToString().Split(',');
            p.Actions=p.Actions.Where(a=>!deniedActions.Contains(p.ModuleCode+":"+a)).ToArray();
        }
        var navigation=new List<NavigationModule>();
        NavigationModule Add(int moduleId,int? parent,string code,string name,string? route,int sort,string icon="",params string[] required)
        {var item=new NavigationModule{ModuleId=moduleId,ParentModuleId=parent,ModuleCode=code,ModuleName=name,Route=route,SortOrder=sort,Icon=icon,IsActive=true,RequiredPermissions=required};navigation.Add(item);return item;}
        Add(100,null,"DASHBOARD","Dashboard","/",1,"home","DASHBOARD:VIEW");
        Add(110,null,"RECEIVE_STOCK","Receive Stock",null,10,"in");Add(111,110,"PURCHASES","Purchase orders & deliveries","/Purchases",10,"","PURCHASES:VIEW");Add(112,110,"NEW_STOCK_RECEIPT","New stock receipt","/Purchases/Create",20,"","PURCHASES:CREATE");Add(113,110,"STOCK_INFLOW","Stock receipt history","/StockInflow",30,"","STOCK_INFLOW:VIEW");Add(114,110,"BARCODES","Barcodes","/Labels",40,"","BARCODES:VIEW");
        Add(120,null,"SELL_BILLING","Sell / Billing",null,20,"document");Add(121,120,"SALES","Sales & invoices","/Sales",10,"","SALES:VIEW");Add(122,120,"NEW_SALE","New sale","/Sales/Create",20,"","SALES:CREATE");
        Add(130,null,"INVENTORY_CONTROL","Inventory",null,30,"box");Add(131,130,"INVENTORY","Current stock","/Inventory",10,"","INVENTORY:VIEW");Add(132,130,"LOW_STOCK","Low stock","/Inventory/LowStock",20,"","INVENTORY:VIEW");Add(137,130,"REPLENISHMENT","Replenishment Planning","/Replenishment",23,"chart","REPLENISHMENT:VIEW");Add(133,130,"STOCK_TRANSFER","Stock transfers","/Transfers",30,"","STOCK_TRANSFER:VIEW");Add(134,130,"STOCK_OUTFLOW","Outflow history","/StockOutflow",40,"","STOCK_OUTFLOW:VIEW");Add(135,130,"SCAN","Scan product / serial","/Scan",50,"","SCAN:VIEW");Add(136,130,"SEARCH","Search records","/Search",60,"","SEARCH:VIEW");
        Add(140,null,"REPORTS","Reports","/Reports",40,"chart","REPORTS:VIEW","GST_REPORTS:VIEW");
        Add(180,null,"EXPENSES","Operating Expenses","/Expenses",35,"document","EXPENSES:VIEW");
        Add(123,120,"QUOTATIONS","Quotations & Estimates","/Quotations",30,"","QUOTATIONS:VIEW");
        Add(150,null,"SERVICE_WARRANTY","Service & Warranty",null,50,"tools");Add(151,150,"SERVICE","Service history","/Service",10,"","SERVICE:VIEW");Add(152,150,"NEW_SERVICE_REQUEST","New service request","/Service/Create",20,"","SERVICE:CREATE");Add(153,150,"PENDING_SERVICES","Pending services","/Service/Pending",30,"","PENDING_SERVICES:VIEW");Add(154,150,"WARRANTY_LOOKUP","Warranty lookup","/Warranty/Lookup",40,"","WARRANTY_LOOKUP:VIEW");Add(155,150,"WARRANTY_CLAIMS","Warranty claims","/Warranty/Claims",50,"","WARRANTY_CLAIMS:VIEW");Add(156,150,"EXPIRING_WARRANTIES","Expiring warranties","/Warranty/Expiring",60,"","EXPIRING_WARRANTIES:VIEW");
        Add(160,null,"SETUP","Setup",null,60,"settings");Add(161,160,"CONFIGURATION","Configuration","/Configuration",20,"","PRODUCTS:VIEW","CATEGORIES:VIEW","BRANDS:VIEW","SUPPLIERS:VIEW","CUSTOMERS:VIEW");Add(162,160,"USER_MANAGEMENT","Users","/Admin/Users",30,"","USER_MANAGEMENT:VIEW");Add(163,160,"ROLE_MANAGEMENT","Roles & permissions","/Admin/Roles",40,"","ROLE_MANAGEMENT:VIEW");Add(164,160,"STORES","Stores","/Admin/Stores",50,"","STORES:VIEW");Add(165,160,"SECURITY","Security settings","/Admin/Security",60,"","SECURITY:VIEW");Add(166,160,"AUDIT_LOGS","Audit logs","/Admin/AuditLogs",70,"","AUDIT_LOGS:VIEW");Add(167,160,"COMPANY_SETTINGS","Company settings","/Settings/Company",80,"","COMPANY_SETTINGS:VIEW");
        if(headers["Test-No-CRM-Navigation"]!="true"){
            Add(170,null,"CRM","Customer Relationships",null,65,"users");Add(171,170,"CRM_OVERVIEW","Customer Overview","/Crm",1,"","CRM:VIEW");Add(172,170,"CRM_ENQUIRIES","Enquiries & Follow-ups","/Crm/Enquiries",2,"","CRM:VIEW");
        }
        return Task.FromResult<UserAccess?>(new(){TwoFactorEnabled=headers["Test-2FA-Enabled"]=="true",TwoFactorRequired=headers["Test-2FA-Required"]=="true",UserId=id,Username=role,FullName="Example "+role,Email="person@example.test",MobileNumber="9876543210",RoleName=role,IsActive=headers["Test-Inactive"]!="true",SecurityVersion=1,MustChangePassword=headers["Test-Force-Password"]=="true",Permissions=perms,NavigationModules=navigation});
    }
    public Task<LoginResult> LoginAsync(LoginRequest r)=>Task.FromResult(new LoginResult());
    public Task<IEnumerable<SecurityUser>> GetUsersAsync(string? term,int offset)=>Task.FromResult<IEnumerable<SecurityUser>>([]);
    public Task<SecurityUser?> GetUserAsync(int id)=>Task.FromResult<SecurityUser?>(null);
    public Task<IEnumerable<ModulePermission>> GetModulesAsync()=>Task.FromResult<IEnumerable<ModulePermission>>([]);
    public Task<IEnumerable<SecurityRole>> GetRolesAsync()=>Task.FromResult<IEnumerable<SecurityRole>>([]);
    public Task<IEnumerable<ModulePermission>> GetRolePermissionsAsync(int id)=>GetModulesAsync();
    public Task<IEnumerable<SecurityAudit>> GetAuditAsync(AuditSearch s,int limit=100)=>Task.FromResult<IEnumerable<SecurityAudit>>([]);
    public Task<int> SaveUserAsync(int? id,SaveSecurityUserRequest r,int actor)=>throw new NotSupportedException();
    public Task SetStatusAsync(int id,UserStatusUpdate r,int actor)=>throw new NotSupportedException();
    public Task SetPermissionsAsync(int id,PermissionUpdate r,int actor)=>throw new NotSupportedException();
    public Task ResetPasswordAsync(int id,ResetPasswordRequest r,int actor)=>throw new NotSupportedException();
    public Task ChangePasswordAsync(int id,ChangePasswordRequest r)=>throw new NotSupportedException();
    public Task SetRolePermissionsAsync(int id,List<ModulePermission> rows,int actor)=>throw new NotSupportedException();
}

