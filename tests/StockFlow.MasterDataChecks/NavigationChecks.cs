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
        foreach(var (url,file) in new[]{("/Sales","Sales"),("/Sales/Create","NewSale"),("/Purchases","Receipts"),("/Labels","Labels"),("/Inventory","Inventory"),("/Customers","Customers"),("/Reports/Activity","Reports"),("/Products","Products"),("/Categories","Categories"),("/Brands","Brands"),("/Suppliers","Suppliers"),("/Products/Create","ProductCreate"),("/Products/Variants","Variants"),("/Categories/Attributes","Attributes")})
        {
            var html=await admin.GetStringAsync(url);var nav=Nav(html);
            foreach(var label in new[]{"Home","Purchases","Sales &amp; Billing","Inventory Overview","Reports","Administration","Service &amp; Warranty","Customer Relationships"})Check(nav.Contains(label),"navigation includes "+label+" on "+url);
            Check(Regex.Matches(nav,"aria-current=\"page\"").Count==1,"exactly one current navigation link on "+url);
            foreach(var moved in new[]{"Products","Categories","Brands","Suppliers","Customers"})Check(Regex.Matches(nav,"href=\"/"+moved+"\"").Count==1,"one sidebar link for "+moved+" on "+url);
            Check(!nav.Contains("Business Partners")&&!nav.Contains("href=\"/Configuration\""),"old Administration groups removed on "+url);
            var family=url.Split('/')[1];
            var expected=family switch{"Products" or "Categories" or "Brands"=>"Inventory","Suppliers"=>"Receive Stock","Customers"=>"Customer Relationships",_=>null};
            if(expected is not null){
                Check(nav.Contains("open=\"open\" data-nav-group=\""+expected+"\"") || Regex.IsMatch(nav,"<details[^>]*\\bopen(?:=\"[^\"]*\")?[^>]*data-nav-group=\""+expected+"\""),"correct workflow parent expanded on "+url);
                Check(!Regex.IsMatch(nav,"<details[^>]*\\bopen(?:=\"[^\"]*\")?[^>]*data-nav-group=\"Setup\""),"Administration inactive on "+url);
            }
            if(url=="/Sales/Create")Check(Regex.IsMatch(nav,"<details[^>]*open[^>]*data-nav-group=\"Sell / Billing\""),"active billing group opens automatically");
            if(url=="/Products")Check(!html.Contains("/css/operational-screens.")&&html.Contains("/css/configuration.")&&!html.Contains("sf-operational-main"),"Configuration excluded from operational restyling");
            await File.WriteAllTextAsync(Path.Combine(output,file+".html"),html);
        }
        using var reader=factory.CreateClient(new(){AllowAutoRedirect=false});reader.DefaultRequestHeaders.Add("Test-Role","Admin");
        reader.DefaultRequestHeaders.Add("Test-Deny-Action","SALES:CREATE");
        var sales=await reader.GetStringAsync("/Sales");
        Check(!Nav(sales).Contains("href=\"/Sales/Create\"")&&!sales.Contains(">New sale</a>"),"view-only billing hides create links and button");
        reader.DefaultRequestHeaders.Remove("Test-Deny-Action");
        reader.DefaultRequestHeaders.Add("Test-Deny","PURCHASES,STOCK_INFLOW,BARCODES,SALES,SERVICE,PENDING_SERVICES,WARRANTY_LOOKUP,WARRANTY_CLAIMS,EXPIRING_WARRANTIES,PRODUCTS,CATEGORIES,BRANDS,SUPPLIERS,CUSTOMERS,CRM,USER_MANAGEMENT,ROLE_MANAGEMENT,STORES,SECURITY,AUDIT_LOGS,COMPANY_SETTINGS,GST_SETTINGS,EWAY_BILLS");
        var restricted=Nav(await reader.GetStringAsync("/Inventory"));
        foreach(var group in new[]{"Receive Stock","Sell / Billing","Service &amp; Warranty","Setup","Customer Relationships"})Check(!restricted.Contains(group),"empty unauthorized group hidden: "+group);
        Check((await reader.GetAsync("/Admin/Users")).Headers.Location?.OriginalString=="/Account/AccessDenied","direct unauthorized setup route still denied");
        foreach(var target in new[]{"Products","Categories","Brands","Suppliers","Customers"})Check((await reader.GetAsync("/"+target)).Headers.Location?.OriginalString=="/Account/AccessDenied","direct moved URL still denied: "+target);
        using var scoped=factory.CreateClient(new(){AllowAutoRedirect=false});scoped.DefaultRequestHeaders.Add("Test-Role","Sales");
        var limited=Nav(await scoped.GetStringAsync("/Customers"));
        Check(limited.Contains("href=\"/Customers\"")&&!limited.Contains("href=\"/Crm\""),"Customer permission does not require or grant CRM access");
        scoped.DefaultRequestHeaders.Add("Test-No-CRM-Navigation","true");
        Check(Nav(await scoped.GetStringAsync("/Customers")).Contains("data-nav-group=\"Customer Relationships\""),"Older navigation metadata preserves customer-only access");
        using var supplier=factory.CreateClient(new(){AllowAutoRedirect=false});supplier.DefaultRequestHeaders.Add("Test-Role","Admin");supplier.DefaultRequestHeaders.Add("Test-Deny","PURCHASES,STOCK_INFLOW,BARCODES,CRM");
        var supplierNav=Nav(await supplier.GetStringAsync("/Suppliers"));
        Check(supplierNav.Contains("data-nav-group=\"Receive Stock\"")&&supplierNav.Contains("href=\"/Suppliers\"")&&!supplierNav.Contains("href=\"/Purchases\""),"Supplier-only access opens Purchases without purchase-order rights");
        Console.WriteLine("Navigation and operational screen checks passed; no database accessed.");
    }
}
