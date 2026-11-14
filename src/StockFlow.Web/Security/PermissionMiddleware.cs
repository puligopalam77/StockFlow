using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Security;

public class PermissionMiddleware
{
    private readonly RequestDelegate next;
    // ASP.NET supplies these services when it creates this class.
    public PermissionMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISecurityRepository repository, PermissionContext access)
    {
        if (context.Request.Path.Equals(new PathString("/error-log")))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant() ?? "";
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (!int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            await Deny(context, 401);
            return;
        }

        access.User = await repository.GetAccessAsync(id);
        if (access.User is null || !access.User.IsActive || context.User.FindFirstValue("SecurityVersion") != access.User.SecurityVersion.ToString())
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await Deny(context, 401);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var sessionRoute = path is "/account/session/activity" or "/account/session/expire";
        var twoFactorRoute = sessionRoute || path is "/account/twofactor" or "/account/logout" or "/account/accessdenied";
        var mfaExempt = StockFlow.Application.Common.SecurityRules.IsMfaExempt(access.User.RoleName);
        if (mfaExempt && path == "/account/twofactor")
        {
            context.Response.Redirect(access.User.MustChangePassword ? "/Account/ChangePassword" : "/Account/Home");
            return;
        }

        var needsChallenge = !mfaExempt && access.User.TwoFactorEnabled && context.User.FindFirstValue("TwoFactorVerified") != "true";
        var needsEnrollment = !mfaExempt && access.User.TwoFactorRequired && !access.User.TwoFactorEnabled && !access.User.MustChangePassword;
        if (path == "/account/twofactor" && access.User.MustChangePassword && !access.User.TwoFactorEnabled)
        {
            context.Response.Redirect("/Account/ChangePassword");
            return;
        }

        if ((needsChallenge || needsEnrollment) && !twoFactorRoute)
        {
            if (path.StartsWith("/api/"))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Complete authenticator verification before continuing."
                });
            }
            else
            {
                context.Response.Redirect("/Account/TwoFactor");
            }

            return;
        }

        if (access.User.MustChangePassword && !sessionRoute && !new[]
        {
            "/account/changepassword",
            "/account/logout",
            "/account/accessdenied",
            "/account/twofactor"
        }.Contains(path) && context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            if (path.StartsWith("/api/"))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Change your password before continuing."
                });
            }
            else
            {
                context.Response.Redirect("/Account/ChangePassword");
            }

            return;
        }

        if (path is "/notifications" or "/api/notifications" or "/api/notifications/read" || path.StartsWith("/account/") || path == "/error" || path == "/api/antiforgery" || path == "/api/branches/context" || path == "/api/help/questions" || path == "/api/help/questions/feedback" || path == "/api/help/voice" || (path == "/api/screenlanguages" && HttpMethods.IsGet(context.Request.Method)) || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        if (path == "/configuration" && HttpMethods.IsGet(context.Request.Method))
        {
            if (new[]
            {
                "PRODUCTS",
                "CATEGORIES",
                "BRANDS",
                "SUPPLIERS",
                "CUSTOMERS",
                "COMPANY_SETTINGS"
            }.Any(m => access.Can(m)))
            {
                await next(context);
            }
            else
            {
                await Deny(context, 403);
            }

            return;
        }

        if(path=="/api/receiving/unit-identifiers" && HttpMethods.IsPost(context.Request.Method))
        {
            if(access.Can("PURCHASES","APPROVE") || access.Can("STOCK_INFLOW","RECEIVE") || access.Can("STOCK_ADJUSTMENT","ADJUST")) await next(context);
            else await Deny(context,403);
            return;
        }
        var (module, action) = Requirement(path, context.Request.Method);
        if (path == "/configuration/notifications" && HttpMethods.IsPost(context.Request.Method)) action = "EDIT";
        if (context.Request.Method == "GET" && (path == "/api/admin/modules" || path == "/api/admin/roles" || path.StartsWith("/api/admin/roles/") && path.EndsWith("/permissions")) && access.Can("USER_MANAGEMENT"))
        {
            module = "USER_MANAGEMENT";
        }

        if (path == "/purchases/create" && context.Request.Query.ContainsKey("id"))
        {
            action = "EDIT";
        }

        if (module is null || !access.Can(module, action))
        {
            await Deny(context, 403);
            return;
        }

        await next(context);
    }

    public static (string? Module, string Action) Requirement(string path, string method)
    {
        var action = method switch
        {
            "POST" => "CREATE",
            "PUT" or "PATCH" => "EDIT",
            "DELETE" => "DELETE",
            _ => "VIEW"
        };
        var p = path.Trim('/').ToLowerInvariant().Split('/');
        if (p[0] == "api")
        {
            p = p.Skip(1).ToArray();
        }

        var key = p.FirstOrDefault() ?? "";
        if(key=="bankaccounts" || key=="settings"&&p.ElementAtOrDefault(1)=="bankaccounts")
            return ("BANK_ACCOUNTS",method=="GET"?"VIEW":p.LastOrDefault() switch{"create"=>"CREATE","edit" or "link"=>"EDIT","assign"=>"ASSIGN","default"=>"SET_DEFAULT","status"=>"DEACTIVATE","open"=>"INITIALIZE","verify"=>"VERIFY","reverse"=>"REVERSE",_=>"VIEW"});
        if(key=="replenishment") return ("REPLENISHMENT",p.Contains("export")?"EXPORT":p.Contains("policy")?"POLICY_EDIT":p.Contains("draft")?"PREPARE_PO":"VIEW");
        if(key=="expenses") return ("EXPENSES",method=="GET"?(p.Contains("export")?"EXPORT":p.Contains("create")?"CREATE":"VIEW"):p.Contains("masters")?"EDIT":p.Contains("attachments")?"ATTACH":p.LastOrDefault() switch{"save" or "record"=>"VIEW","correct"=>"EDIT","post"=>"POST","pay"=>"PAY","refund" or "reverse"=>"REVERSE","cancel"=>"CANCEL",_=>"CREATE"});
        if(key=="shopmasters" || key=="admin"&&p.ElementAtOrDefault(1)=="shopdata") return ("COMPANY_SETTINGS",method=="GET"?"VIEW":"EDIT");
        if(key=="ewaybills") return ("EWAY_BILLS",p.Contains("provider")?"CONFIGURE_PROVIDER":p.Contains("usage")?"VIEW_USAGE":p.Contains("export")?"EXPORT":p.Contains("validate")?"VALIDATE":p.Contains("external")?"RECORD_EXTERNAL":method=="PUT"?"EDIT":method=="POST"?"PREPARE":"VIEW");
        if(key=="configuration" && (p.ElementAtOrDefault(1)=="whatsapp" || p.ElementAtOrDefault(1)=="notifications" && p.ElementAtOrDefault(2)=="whatsapp"))
            return ("COMPANY_SETTINGS",method=="GET"?"VIEW":"EDIT");
        if(key=="crm")
            return ("CRM",method=="GET"?"VIEW":p.Contains("follow-ups")?"FOLLOW_UP":p.Contains("notes")||p.Contains("preferences")||method=="PUT"?"EDIT":"CREATE");
        if (key == "gst")
        {
            if(p.ElementAtOrDefault(1)=="settings"&&p.ElementAtOrDefault(2)=="catalog")
            {
                if(p.Contains("billing-defaults"))return ("COMPANY_SETTINGS","EDIT");
                if(p.Contains("export"))return ("GST_SETTINGS","EXPORT_TAX");
                if(p.Contains("import")||p.Contains("template"))return ("GST_SETTINGS","IMPORT_TAX");
                if(p.Contains("review"))return ("GST_SETTINGS","APPROVE_TAX");
                if(p.Contains("end"))return ("GST_SETTINGS","OVERRIDE_TAX");
            }
            return p.ElementAtOrDefault(1) == "settings"
                ? ("GST_SETTINGS", method == "GET" ? "VIEW" : "EDIT")
                : ("GST_REPORTS", p.Contains("export") ? "EXPORT" : p.Contains("review") ? "REVIEW" : method == "GET" ? "VIEW" : "CREATE");
        }
        if (key == "admin" && p.ElementAtOrDefault(1) == "gst") return ("GST_SETTINGS", "VIEW");
        if (key == "reports" && p.ElementAtOrDefault(1) == "gst") return ("GST_REPORTS", "VIEW");
        if(method=="GET" && p.ElementAtOrDefault(1)=="edit" && new[]{"products","categories","brands","suppliers","customers"}.Contains(key)) action="EDIT";
        if(method=="POST" && (key=="categories" || key=="suppliers") && p.LastOrDefault()=="status") action="DELETE";
        if(key=="quotations")
            return ("QUOTATIONS",p.Contains("print")?"PRINT":p.Contains("export")?"EXPORT":p.Contains("checkout")?"CONVERT":p.Contains("state")?"STATUS":p.Contains("preview")?"VIEW":p.Contains("edit")?"EDIT":p.Contains("create")?"CREATE":method=="GET"?"VIEW":method=="PUT"?"EDIT":"CREATE");
        string? module = key switch
        {
            "" or "index" => "DASHBOARD",
            "users" => "USER_MANAGEMENT",
            "products" => "PRODUCTS",
            "categories" => "CATEGORIES",
            "brands" => "BRANDS",
            "suppliers" => "SUPPLIERS",
            "customers" => "CUSTOMERS",
            "purchases" => "PURCHASES",
            "inventory" => "INVENTORY",
            "help" when p.ElementAtOrDefault(1) == "stock" => "INVENTORY",
            "sales" => "SALES",
            "invoices" => "INVOICES",
            "stockinflow" => "STOCK_INFLOW",
            "stockoutflow" => "STOCK_OUTFLOW",
            "scan" => "SCAN",
            "search" => "SEARCH",
            "reports" => "REPORTS",
            "cashclosing" => "REPORTS",
            "cashinhand" => "REPORTS",
            "settings" => "COMPANY_SETTINGS",
            "business-profile" => "COMPANY_SETTINGS",
            "screenlanguages" or "configuration" => "COMPANY_SETTINGS",
            "database" or "health" => "SECURITY",
            "service" => p.ElementAtOrDefault(1) == "pending" ? "PENDING_SERVICES" : "SERVICE",
            "warranty" => p.ElementAtOrDefault(1) == "claims" ? "WARRANTY_CLAIMS" : p.ElementAtOrDefault(1) == "expiring" ? "EXPIRING_WARRANTIES" : "WARRANTY_LOOKUP",
            "admin" => p.ElementAtOrDefault(1) switch
            {
                "users" => "USER_MANAGEMENT",
                "auditlogs" => "AUDIT_LOGS",
                _ => "SECURITY"
            },
            "dashboard" => p.ElementAtOrDefault(1) switch
            {
                "users" => "USER_MANAGEMENT",
                "search" => "SEARCH",
                "recent-sales" => "SALES",
                "recent-services" => "SERVICE",
                "movements" => "INVENTORY",
                "low-stock" or "category-stock" or "inventory-summary" => "INVENTORY",
                "recent-inflow" => "STOCK_INFLOW",
                "recent-outflow" => "STOCK_OUTFLOW",
                _ => "DASHBOARD"
            },
            "stock-receipts" => "STOCK_INFLOW",
            _ => null
        };
        // Cash closing also checks verified owner/administrator membership in its endpoint.
        if (key is "cashclosing" or "cashinhand") action = "VIEW";
        if (key == "operations")
        {
            module = p.ElementAtOrDefault(1) switch
            {
                "sales" => "SALES",
                "purchases" => "PURCHASES",
                "stock-counts" => "STOCK_ADJUSTMENT",
                "service" => "SERVICE",
                "warranty-claims" => "WARRANTY_CLAIMS",
                "reports" => "REPORTS",
                _ => null
            };
            if (method != "GET")
            {
                action = p.LastOrDefault() switch
                {
                    "payments" => module == "PURCHASES" ? "PAY" : "COLLECT",
                    "returns" => "RETURN",
                    "fulfilments" or "trade-ins" => "FULFIL",
                    "rmas" => "UPDATE",
                    _ => module == "STOCK_ADJUSTMENT" ? "ADJUST" : action
                };
            }
        }

        if (key == "sales" && (p.ElementAtOrDefault(1) == "invoice" || p.ElementAtOrDefault(2) == "invoice"))
        {
            module = "INVOICES";
        }

        if (p.ElementAtOrDefault(1) == "create" || key == "admin" && p.ElementAtOrDefault(2) == "create")
        {
            action = "CREATE";
        }

        if (method == "POST" && (p.Contains("reset-password") || p.Contains("permissions") || p.Contains("parts")))
        {
            action = "EDIT";
        }

        if (method != "GET" && p.Contains("status") && key == "admin")
        {
            action = "DELETE";
        }

        if (method == "POST" && key == "settings")
        {
            action = "EDIT";
        }

        if (method != "GET" && (p.Contains("complete") || p.Contains("adjustments") || p.Contains("receipts")))
        {
            action = "APPROVE";
        }

        if (method == "POST" && key == "products" && p.Contains("barcode"))
        {
            action = "EDIT";
        }

        if (p.Contains("export"))
        {
            action = "EXPORT";
        }

        if (key == "branches")
        {
            module = p.Contains("comparison") || p.Contains("summary") ? "REPORTS" : "STORES";
        }

        if (key == "receiving")
        {
            var opening = path.EndsWith("/opening") || path.EndsWith("/opening/import-key");
            module = opening ? "STOCK_ADJUSTMENT" : "STOCK_INFLOW";
            action = method == "GET" ? "VIEW" : opening ? "ADJUST" : "RECEIVE";
        }

        if (key == "labels" || key == "receiving" && p.Contains("labels") || key == "products" && p.Contains("item-labels"))
        {
            module = "BARCODES";
            action = method == "GET" ? "VIEW" : p.LastOrDefault() is "print" or "reprint" or "generate" ? p.Last().ToUpperInvariant() : "VIEW";
        }

        if (key == "admin" && p.ElementAtOrDefault(1) == "stores")
        {
            module = "STORES";
        }

        if (key == "transfers")
        {
            module = "STOCK_TRANSFER";
            if (method == "POST" && p.LastOrDefault() is "approve" or "discrepancy")
            {
                action = "APPROVE";
            }

            if (method == "POST" && p.LastOrDefault() is "dispatch" or "receive" or "cancel")
            {
                module = "TRANSFER_" + p.Last().ToUpperInvariant();
                action = "APPROVE";
            }
        }

        if (key == "admin" && p.ElementAtOrDefault(1) == "roles")
        {
            module = "ROLE_MANAGEMENT";
            if (method != "GET" || p.ElementAtOrDefault(2) is "create" or "edit")
            {
                action = "MANAGE_PERMISSIONS";
            }
        }

        if (key == "admin" && p.ElementAtOrDefault(1) == "store-options")
        {
            module = "USER_MANAGEMENT";
        }

        if (key == "admin" && p.ElementAtOrDefault(1) == "security")
        {
            module = "SECURITY";
            if (method != "GET")
            {
                action = "MANAGE";
            }
        }

        if (key == "admin" && p.Contains("reset-password"))
        {
            action = "RESET_PASSWORD";
        }

        if (key == "admin" && p.Contains("reset-authenticator"))
        {
            action = "RESET_AUTHENTICATOR";
        }

        if (key == "admin" && p.Contains("users") && p.Contains("permissions") && method != "GET")
        {
            action = "MANAGE_PERMISSIONS";
        }

        if (key == "branches" && p.Contains("users") && method != "GET")
        {
            action = "MANAGE_ACCESS";
        }

        if (key == "transfers" && method == "POST" && p.LastOrDefault() is "dispatch" or "receive" or "cancel")
        {
            module = "STOCK_TRANSFER";
            action = p.Last().ToUpperInvariant();
        }

        if (key == "transfers" && p.Contains("dispatch-units"))
        {
            module = "STOCK_TRANSFER";
            action = "DISPATCH";
        }

        if (key == "transfers" && p.LastOrDefault() == "variants")
        {
            module = "STOCK_TRANSFER";
            action = "CREATE";
        }

        if (key == "products" && p.ElementAtOrDefault(1) == "variants" && !p.Contains("item-labels"))
        {
            module = "PRODUCT_VARIANTS";
        }

        if (key == "products" && p.Contains("barcode"))
        {
            module = "BARCODES";
            action = method == "GET" ? "VIEW" : "GENERATE";
        }

        if (key == "products" && p.LastOrDefault() is "print" or "reprint")
        {
            module = "BARCODES";
            action = p.Last().ToUpperInvariant();
        }

        if (key == "inventory" && p.Contains("adjustments"))
        {
            module = "STOCK_ADJUSTMENT";
            action = method == "GET" ? "VIEW" : "ADJUST";
        }

        if (key == "purchases" && p.Contains("changes") && method != "GET") action = "EDIT";

        if (key == "purchases" && p.Contains("receipts") && method != "GET")
        {
            module = "STOCK_INFLOW";
            action = "RECEIVE";
        }

        if (key == "scan" && path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            action = "SCAN";
        }

        if (module == "INVOICES" && action == "VIEW")
        {
            action = "PRINT";
        }

        if (key == "service" && method == "PUT")
        {
            action = "VIEW"; // Record-specific checks in controller.
        }

        if (key == "warranty" && p.Contains("claims") && method == "PUT")
        {
            action = "VIEW";
        }

        if (key == "sales" && p.ElementAtOrDefault(1) == "billing")
        {
            action = "CREATE";
        }

        if (key == "sales" && p.ElementAtOrDefault(1) == "billing" && p.ElementAtOrDefault(2) == "approvals")
        {
            action = "DISCOUNT_APPROVE";
        }

        return (module, action);
    }

    private static async Task Deny(HttpContext c, int code)
    {
        if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/account/session"))
        {
            c.Response.StatusCode = code;
            await c.Response.WriteAsJsonAsync(new
            {
                message = code == 401 ? "Sign in again." : "You do not have permission for this action."
            });
        }
        else
        {
            c.Response.Redirect(code == 401 ? "/Account/Login" : "/Account/AccessDenied");
        }
    }
}
