namespace StockFlow.Web.Help;

public record HelpLink(string Label, string Url);
public record HelpArticle(string Id, string Title, string Question, string[] Screens,
    string Content, HelpLink[] Links);

// Embed the reviewed staff guides so development and published builds use the same content.
public static class HelpCatalog
{
    public static readonly HelpArticle[] Articles =
    [
        Article("application-features", "More StockFlow features", "How do I use other StockFlow features?", ["/Reports", "/Admin", "/Transfers", "/CashClosing", "/Inventory", "/Settings/Company"],
            [new("Open Reports", "/Reports"),new("Stock on hand", "/Inventory"),new("Low stock", "/Inventory/LowStock"),new("Expiring warranties", "/Warranty/Expiring"),new("Pending Repairs", "/Service/Pending"),new("Staff accounts", "/Admin/Users"),new("Roles and permissions", "/Admin/Roles"),new("Stores and locations", "/Admin/Stores"),new("Stock transfers", "/Transfers"),new("Daily cash closing", "/CashClosing"),new("Company settings", "/Settings/Company")]),
        Article("products", "Adding products", "How do I add a product?", ["/Products", "/Configuration"],
            [new("Open Products", "/Products"), new("Receive new stock", "/Purchases/Create")]),
        Article("receiving-stock", "Receiving stock", "How do I receive goods or place an order?", ["/Purchases", "/Receiving", "/StockInflow"],
            [new("Receive new stock", "/Purchases/Create"), new("Purchase orders & deliveries", "/Purchases"), new("Stock receipt history", "/StockInflow")]),
        Article("billing", "Creating bills and taking payment", "How do I make a bill or print an invoice?", ["/Sales"],
            [new("Start a new sale", "/Sales/Create"), new("Find a sale or invoice", "/Sales")]),
        Article("returns", "Customer returns and refunds", "How do I return an item or give a refund?", ["/Sales/Details"],
            [new("Find the original sale", "/Sales")]),
        Article("warranty", "Checking warranty and recording claims", "How do I check warranty or record a claim?", ["/Warranty", "/Service"],
            [new("Open Check warranty", "/Warranty/Lookup")]),
        Article("mobile-and-devices", "Android app, camera and microphone", "How do I use the camera or microphone in StockFlow?", [], []),
        Article("common-problems", "Common problems", "What should I do when a scan or save fails?", [],
            [new("Check purchase history", "/Purchases"), new("Find a sale or invoice", "/Sales")])
    ];

    private static HelpArticle Article(string id, string title, string question, string[] screens, HelpLink[] links)
    {
        using var stream = typeof(HelpCatalog).Assembly.GetManifestResourceStream($"HelpContent.{id}.md")
            ?? throw new InvalidOperationException($"Missing staff help article: {id}");
        using var reader = new StreamReader(stream);
        return new(id, title, question, screens, reader.ReadToEnd(), links);
    }
}
