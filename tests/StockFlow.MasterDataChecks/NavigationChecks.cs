using System.Text.RegularExpressions;

namespace StockFlow.MasterDataChecks;

static class NavigationChecks
{
    public static async Task Run(Factory factory)
    {
        static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
        static string Nav(string html){var start=html.IndexOf("<nav class=\"sf-nav sf-shop-nav\"");return html[start..(html.IndexOf("</nav>",start)+6)];}
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/navigation-ui-fixtures");Directory.CreateDirectory(output);
        using var admin=factory.CreateClient(new(){AllowAutoRedirect=false});admin.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var (url,file) in new[]{("/Sales","Sales"),("/Sales/Create","NewSale"),("/Purchases","Receipts"),("/Labels","Labels"),("/Inventory","Inventory"),("/Customers","Customers"),("/Reports/Activity","Reports"),("/Products","Configuration")})
        {
            var html=await admin.GetStringAsync(url);var nav=Nav(html);
            foreach(var label in new[]{"Dashboard","Receive Stock","Sell / Billing","Stock on hand","Reports","Setup","Repairs &amp; warranty"})Check(nav.Contains(label),"navigation includes "+label+" on "+url);
            Check(Regex.Matches(nav,"aria-current=\"page\"").Count==(url=="/Labels"?0:1),"current navigation link matches visible menu on "+url);
            Check(!nav.Contains("href=\"/Receiving\"")&&!nav.Contains("href=\"/Labels\""),"receiving and label shortcuts removed from navigation on "+url);
            if(url=="/Sales/Create")Check(Regex.IsMatch(nav,"<details[^>]*open[^>]*data-nav-group=\"Sell / Billing\""),"active billing group opens automatically");
            if(url=="/Products")Check(!html.Contains("/css/operational-screens.")&&html.Contains("/css/configuration.")&&!html.Contains("sf-operational-main"),"Configuration excluded from operational restyling");
            await File.WriteAllTextAsync(Path.Combine(output,file+".html"),html);
        }
        using var reader=factory.CreateClient(new(){AllowAutoRedirect=false});reader.DefaultRequestHeaders.Add("Test-Role","Admin");
        reader.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");
        var sales=await reader.GetStringAsync("/Sales");
        Check(!Nav(sales).Contains("href=\"/Sales/Create\"")&&!sales.Contains(">New sale</a>"),"view-only billing hides create links and button");
        reader.DefaultRequestHeaders.Remove("Test-Deny-Action");
        reader.DefaultRequestHeaders.Add("Test-Deny","PURCHASES,STOCK_INFLOW,BARCODES,SALES,SERVICE,PENDING_SERVICES,WARRANTY_LOOKUP,WARRANTY_CLAIMS,EXPIRING_WARRANTIES,PRODUCTS,CATEGORIES,BRANDS,SUPPLIERS,CUSTOMERS,USER_MANAGEMENT,ROLE_MANAGEMENT,STORES,SECURITY,AUDIT_LOGS,COMPANY_SETTINGS");
        var restricted=Nav(await reader.GetStringAsync("/Inventory"));
        foreach(var group in new[]{"Receive Stock","Sell / Billing","Repairs &amp; warranty","Setup"})Check(!restricted.Contains(group),"empty unauthorized group hidden: "+group);
        Check((await reader.GetAsync("/Admin/Users")).Headers.Location?.OriginalString=="/Account/AccessDenied","direct unauthorized setup route still denied");
        Console.WriteLine("Navigation and operational screen checks passed; no database accessed.");
    }
}
