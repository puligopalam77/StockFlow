namespace StockFlow.Web.Help;

public record HelpLink(string Label, string Url);
public record HelpArticle(string Id, string Title, string Question, string[] Screens,
    string Content, HelpLink[] Links);

// Embed the reviewed staff guides so development and published builds use the same content.
public static class HelpCatalog
{
    // Use the same approved passages for the browser, local search and model.
    // Removing navigation links alone would still expose the full unauthorized guide.
    public static HelpArticle[] ForUser(StockFlow.Web.Security.PermissionContext access)
    {
        var sections = HelpAiService.Sections(access);
        var visibleArticles = new List<HelpArticle>();

        foreach (var article in Articles)
        {
            var permitted = sections.Where(section => section.ArticleId == article.Id).ToArray();
            if (permitted.Length == 0) continue;

            // Section zero is the article introduction. Without it, use the
            // first permitted heading so a viewer is not offered a create guide.
            var introductionId = article.Id + ":0";
            var hasIntroduction = permitted.Any(section => section.Id == introductionId);
            var title = hasIntroduction ? article.Title : permitted[0].Title;
            var paragraphs = permitted.Select(section => section.Id == introductionId
                ? section.Content
                : "## " + section.Title + "\n\n" + section.Content);

            visibleArticles.Add(article with
            {
                Title = title,
                Question = hasIntroduction ? article.Question : permitted[0].Title,
                Content = "# " + title + "\n\n" + string.Join("\n\n", paragraphs),
                Links = article.Links.Where(link => access.CanVisit(link.Url)).ToArray()
            });
        }

        return visibleArticles.ToArray();
    }

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
