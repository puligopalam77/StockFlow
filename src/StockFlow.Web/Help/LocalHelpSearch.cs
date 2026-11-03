using System.Text.RegularExpressions;
using StockFlow.Web.Security;

namespace StockFlow.Web.Help;

// Local approved guides remain usable when no AI provider is configured.
public static class LocalHelpSearch
{
    private record Topic(string Pattern, string Article, string? Title = null);
    private static readonly Topic[] Topics = [
        new(@"microphone|\bmic\b|transcrib|speak replies|read reply|voice question|speech|voice recording", "mobile-and-devices", "Ask using the microphone"),
        new(@"camera|use code", "mobile-and-devices", "Scan using the camera"),
        new(@"android|\bapk\b|connect to stockflow|shop (?:url|address)|localhost|mobile app", "mobile-and-devices", "Connect the Android app"),
        new(@"save as pdf|save file|bluetooth|usb printer|print service|download.*document|upload.*file", "mobile-and-devices", "Upload files, download documents and print"),
        new(@"correct.*stock|stock.*correct|stock count|count.*stock|count.*shel(?:f|ves)|stock adjustment|damaged.*stock", "application-features", "Correct stock quantity and count stock"),
        new(@"\blogo\b|shop details|bill settings|invoice prefix|receipt prefix|tax mode|payment terms|salesperson|terms.*conditions|invoice footer", "application-features", "Shop details & bill settings"),
        new(@"(?:export|search|filter).*(?:sale|invoice)|(?:sale|invoice).*(?:export|search|filter)", "billing", "Search and export sales invoices"),
        new(@"shop summary|product sales.*profit|more reports", "application-features", "Reports"),
        new(@"stock receipt history|purchase orders.*deliveries|receipt details|item movements", "receiving-stock", "Find purchase orders and stock receipt history"),
        new(@"what can (?:i|we) ask|quick assist|stockbot|stockflow assistant|supported questions", "mobile-and-devices", "Ask supported questions"),
        new(@"timeout|timed out|unconfirmed|invalid username|inactive account|forgot password|scan.*(fail|not|work)|validation|mandatory|phone number|mobile number|failed confirmation|(details|contact).*(administrator|super admin)|ఖాతా|సంప్రదించ", "common-problems"),
        new(@"role|permission|access rights", "application-features", "Roles and permissions"),
        new(@"staff|employee|user account|new user", "application-features", "Staff accounts"),
        new(@"language|telugu|hindi|translation", "application-features", "Language selection"),
        new(@"password|authenticator|security|sign in|login", "application-features", "Password and account security"),
        new(@"cash closing|cash drawer|count.*cash|opening cash|close.*cash", "application-features", "Cash closing"),
        new(@"transfer|move.*stock", "application-features", "Stock transfers"),
        new(@"repair|technician|\bservice\b", "application-features", "Repairs"),
        new(@"company|business details|invoice logo|gst number", "application-features", "Shop details & bill settings"),
        new(@"store|warehouse|location|branch", "application-features", "Stores and locations"),
        new(@"report|profit|margin|analysis", "application-features", "Reports"),
        new(@"how many warranty months|register.*product|new model|product picture|barcode.*serial|item code", "products"),
        new(@"(check|view|find|see).*(completed sales|sales today|today.s sales|sales count)", "billing", "Check today's completed sales"),
        new(@"warranty|claim|coverage|వారంటీ|वारंटी", "warranty"),
        new(@"return|refund|exchange|वापस", "returns"),
        new(@"receiv|reciev|purchase|stock receipt|partial delivery|damaged goods|supplier invoice|serials.*enter|స్టాక్|प्राप्त", "receiving-stock"),
        new(@"bill|invoice|sell|sale|sold|credit|payment|discount|tax|walk.in|print|receipt|choose.*customer|బిల్లు", "billing"),
        new(@"supplier|customer|contact", "application-features", "Suppliers and customers"),
        new(@"inventory|stock", "application-features", "Inventory and low stock"),
        new(@"product|item code|barcode|serial|sku|model|size|brand|category|ఉత్పత్తి", "products")
    ];

    public static HelpAnswer Answer(string question, PermissionContext access)
    {
        var text = question.ToLowerInvariant();
        if (Regex.IsMatch(text, @"weather|poem|cricket|politic|recipe|movie|gst law|reveal|credentials|ignore.*rules|admin password"))
            return new("unsupported", "This question is outside StockFlow help. Contact Super Admin.", [], []);
        if (Regex.IsMatch(text, @"^(delete|create|reset|change|refund|send|show every customer)\b") && !text.StartsWith("how"))
            return new("clarify", "I can explain StockFlow screens, but cannot perform transactions or account changes. Ask how to complete the task, or use the permitted screen.", [], []);
        var topic = Topics.FirstOrDefault(item => Regex.IsMatch(text, item.Pattern));
        if (topic is null)
            return Regex.IsMatch(text,@"stockflow|this app|this tool|trade.in|adjustment|backup|restore|audit|notification|opening stock|stock take")
                ? new("clarify", "This is a StockFlow topic. Please specify the screen name and what you want to do so I can find verified instructions. Browse help articles for the available workflows.", [], [])
                : new("unsupported", "This question is outside the available StockFlow help. Contact Super Admin.", [], []);
        var candidates = HelpAiService.Sections(access).Where(section => section.ArticleId == topic.Article &&
            (topic.Title is null || section.Title == topic.Title)).ToArray();
        if (candidates.Length == 0)
            return new("restricted", "This StockFlow feature requires permissions your account does not have. Contact Super Admin for access.", [], []);
        if (Regex.IsMatch(text, @"profit") && !Regex.IsMatch(text, @"\bhow\s+(do|can|to|should)\b|\bwhere\b") ||
            Regex.IsMatch(text, @"^(how much|how many|show me|what is the total).*(sold|stock|amount|sales|balance|dues)"))
            return new("clarify", "I cannot retrieve that specific total yet. Please specify the information and period you need. I can answer supported sales, stock, repair and warranty totals directly.", [], []);
        var words = Regex.Matches(text, @"[\p{L}\p{N}]+").Select(match => match.Value)
            .Where(word => word.Length > 2 && !new[] {"how","what","does","can","the","for","and","enter","today","stockflow"}.Contains(word)).Distinct().ToArray();
        var selected = candidates.OrderByDescending(section => words.Sum(word =>
            (section.Title.Contains(word, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
            (section.Content.Contains(word, StringComparison.OrdinalIgnoreCase) ? 1 : 0)))
            .ThenBy(section => section.Id.EndsWith(":0") ? 1 : 0).Take(1).ToArray();
        var links = HelpCatalog.Articles.Single(article => article.Id == topic.Article).Links
            .Where(link => access.CanVisit(link.Url) && (topic.Title is null || selected[0].Content.Contains(link.Url)))
            .ToArray();
        return new("answered", "Here are the approved StockFlow instructions for this topic. Open the source for the complete guide.", selected, links);
    }
}
