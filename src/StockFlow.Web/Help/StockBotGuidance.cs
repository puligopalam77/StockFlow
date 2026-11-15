using System.Text.RegularExpressions;
using StockFlow.Application.Common;
using StockFlow.Web.Security;

namespace StockFlow.Web.Help;

public record StockBotStep(string Title, string Instruction, string? Target = null);
public record StockBotField(string Id, string Title, string Explanation, string Example, string[] Captions);
public record StockBotGuide(string Id, string Title, string Purpose, string Route, StockBotStep[] Steps, string[] FieldIds);
public record StockBotError(string Code, string Title, string Explanation);

// Reviewed UI instructions, never model-generated selectors, actions or URLs.
public sealed class StockBotGuidance(PermissionContext access, BusinessScope scope)
{
    private record Entry(string Page, string Module, string Action, StockBotGuide Guide);
    private static StockBotStep Step(string title, string text, string? target = null) => new(title, text, target);
    private static Entry CreateEntry(string id, string page, string route, string module, string action, string title, string purpose, string[] fields, params StockBotStep[] steps)
        => new(page, module, action, new(id, title, purpose, route, steps, fields));
    private static readonly Entry[] Entries = [
        CreateEntry("sales.new","/Sales/Create","/Sales/Create","SALES","CREATE","New Sale","Select the exact items, review the bill, choose one payment method and record a completed sale.",
            ["selling-price","stock-location","payment","gst"],
            Step("Choose your shop","Check the active shop and stock location before building the bill."),
            Step("Select items","Scan a barcode or search for the exact product or variant. Check available quantity; select serial units when this shop supports them.","#sale-scan"),
            Step("Review the customer and bill","Choose the customer where needed. Review quantity, price, permitted discounts and tax before payment.","#customer-current"),
            Step("Choose payment","Choose one supported payment method and pay the full bill total. Current New Sale does not accept new credit or split checkout.","#payment-methods"),
            Step("Review and complete yourself","Review the final total, then use Complete Sale & Generate Bill yourself. StockBot will never submit the bill. Save Draft or Hold Bill does not complete a sale.")),
        CreateEntry("sales.history","/Sales/Index","/Sales","SALES","VIEW","Sales history","Find completed bills, check recorded payments and open the original invoice for returns.",["payment"],
            Step("Find the original bill","Search by invoice number, customer or date. Use matching dates when checking totals."),
            Step("Open the bill","Review saved items, payments and balance. Payment collection and sales revenue are different figures."),
            Step("Use permitted actions","Print, collect an older balance or start a return only when the action is available to your account.")),
        CreateEntry("sales.details","/Sales/Details","/Sales","SALES","VIEW","Bill details and returns","Review the saved invoice and its permitted payment, return and refund actions.",["payment","selling-price"],
            Step("Check the original invoice","Confirm the customer and exact sold item or serial before recording a return."),
            Step("Check return eligibility","Review the remaining returnable quantity and original prices. StockBot cannot approve a return or promise a refund."),
            Step("Review and confirm yourself","Use the available return or refund action, check its effect, and confirm it yourself.")),
        CreateEntry("inventory.products","/Products/Index","/Products","PRODUCTS","VIEW","Products","Maintain product records; a product record alone does not add stock.",["hsn","minimum-stock","purchase-price","selling-price"],
            Step("Search first","Check for the same product, SKU or barcode before adding a duplicate."),
            Step("Open a product","Review the saved details and exact variants. Use actions permitted for your role."),
            Step("Receive actual quantities","Use stock receiving or opening stock to record goods; do not enter the same stock twice.")),
        CreateEntry("inventory.products.create","/Products/Index","/Products?mode=create","PRODUCTS","CREATE","Add your first product","Create the product and its variants, then receive stock separately.",["hsn","minimum-stock","purchase-price","selling-price"],
            Step("Open product registration","Use Inventory → Products → Add product. If a different tab is open, navigate there using the permitted link."),
            Step("Enter basic details","Enter the product name, category, unit and supported tracking method. Choose the brand when applicable.","#quick-product-name"),
            Step("Set up variants","Use Variants & Attributes for the exact size, colour or other supported options. Give each sellable variant a distinct SKU.","#registration-tab-variants"),
            Step("Review pricing and minimum stock","Check purchase/selling prices and the minimum-stock threshold. Use the shop's approved tax configuration.","#registration-tab-pricing"),
            Step("Review and save yourself","Review Product Details & Review, then save yourself. Receive stock separately to make quantities available.","#registration-tab-media")),
        CreateEntry("purchases.new","/Purchases/Create","/Purchases/Create","PURCHASES","CREATE","Receive supplier stock","Record goods received from a supplier, including quantities, costs and supported serial details.",["purchase-price","opening-stock","stock-location","gst"],
            Step("Check the source","Choose the authorized receiving location, supplier and supplier bill details."),
            Step("Choose exact items","Select the product and exact variant. Confirm unit and received quantity."),
            Step("Review damage and tracking","Record damaged quantities and required serials. An order by itself does not add stock."),
            Step("Review and receive yourself","Check costs, tax and totals, then confirm the receipt yourself. Do not receive the same supplier delivery twice.")),
        CreateEntry("purchases.history","/Purchases/Index","/Purchases","PURCHASES","VIEW","Purchase orders and deliveries","Review orders, received goods, supplier payments and returns.",["purchase-price"],
            Step("Find the purchase","Search or filter the supplier order or delivery."),Step("Check its status","Draft or pending quantities are not received stock. Open the original purchase for permitted actions.")),
        CreateEntry("inventory.stock","/Inventory/Index","/Inventory","INVENTORY","VIEW","Stock on hand","Check available quantities within your current authorized shop and location.",["minimum-stock","opening-stock","stock-location"],
            Step("Search an exact item","Use product name, item code or barcode; check the exact variant."),Step("Review quantities","Available serial units and the recorded balance can differ. Review a discrepancy before promising goods."),Step("Correct carefully","Use an authorized stock-count or adjustment workflow after a physical count. This guide does not change stock.")),
        CreateEntry("inventory.low-stock","/Inventory/LowStock","/Inventory/LowStock","INVENTORY","VIEW","Low stock","Find variants at or below their configured minimum-stock thresholds.",["minimum-stock"],
            Step("Review the shortage","Check quantity and the minimum threshold for the correct variant and location."),Step("Check pending purchases","Review goods already ordered before preparing another purchase. Minimum-stock alerts are not demand forecasts.")),
        CreateEntry("settings.users","/Admin/Users","/Admin/Users","USER_MANAGEMENT","VIEW","Staff accounts and access","Manage authorized staff accounts, assigned shops and saved roles.",["role","username","stock-location"],
            Step("Find the staff account","Search by name, username, mobile or email."),Step("Use an authorized action","Choose New User or an available account action. Owner and protected accounts retain their existing restrictions.")),
        CreateEntry("settings.users.create","/Admin/Users","/Admin/Users","USER_MANAGEMENT","CREATE","Register a staff account","Create an account, choose its assigned shop and role, and review access before saving.",["role","username","stock-location"],
            Step("Enter staff information","Open New User and enter full name, mobile, email and a unique username.","#full-name"),Step("Assign shop and role","Choose the authorized shop and saved role. Check Access Preview before continuing.","#primary-store"),Step("Set login security","Set and confirm the password yourself; never share it with StockBot. Review the next-login password-change option.","#initial-password"),Step("Review and create yourself","Check account status and access, then choose Create User yourself. Future saved-role changes apply to role-linked staff.")),
        CreateEntry("settings.users.edit","/Admin/Users","/Admin/Users","USER_MANAGEMENT","EDIT","Edit a staff account","Review an existing staff account and its authorized shop/role assignments.",["role","username","stock-location"],
            Step("Review the selected account","Confirm the correct staff member before changing access.","#full-name"),Step("Review assignments","Check shop and role access. Security changes can end affected sessions.","#primary-store"),Step("Save changes yourself","Review and save yourself. StockBot does not change accounts or permissions.")),
        CreateEntry("expenses.list","/Expenses/Index","/Expenses","EXPENSES","VIEW","Expenses","Review recorded business expenses and open the permitted expense workflow.",["cash"],Step("Choose dates and filters","Review the relevant shop and period."),Step("Open a record","Inspect recorded expense details; posting, payment and reversal require their own permissions.")),
        CreateEntry("settings.gst","/Admin/Gst","/Admin/Gst","GST_SETTINGS","VIEW","GST settings","Review the business tax registration and saved shop tax configuration.",["gst","hsn"],Step("Review the legal business","Check the legal-business and shop registration mapping."),Step("Use approved configuration","Have authorized staff complete required registered or unregistered-business setup. StockBot does not decide tax rates or legal applicability.")),
        CreateEntry("barcodes.manage","/Labels/Index","/Labels","BARCODES","VIEW","Barcodes","Review or print barcode labels for recorded stock lots.",[],Step("Choose received stock","Open the relevant received lot or saved barcode workflow."),Step("Generate or print permitted labels","Only generate codes when your role allows it. Printing labels does not receive goods or increase stock.")),
        CreateEntry("reports.summary","/Reports/Activity","/Reports/Activity","REPORTS","VIEW","Shop summary","Review matching stock, sales, collections and outstanding figures for a selected period.",["cash","stock-location"],Step("Select scope and dates","Choose only an authorized location or consolidated scope."),Step("Compare the same metrics","Sales revenue, payment collections, returns and profit are different figures. Open the source records to reconcile differences."))
    ];
    public static readonly StockBotField[] Fields = [
        new("hsn","HSN code","The goods-classification code used by the configured GST workflow. It does not identify an individual product or its stock.","Use the classification approved for your goods; StockBot does not assign HSN codes or tax rates.",["HSN","HSN code"]),
        new("minimum-stock","Minimum stock","The threshold at which StockFlow identifies low stock for the configured variant and location.","If minimum stock is 10, a balance of 10 or below can appear in Low stock. Review pending purchases before ordering.",["Minimum stock","Minimum stock quantity","Minimum quantity","Low stock alert","Reorder level"]),
        new("opening-stock","Opening stock","Stock already held when you begin tracking it in StockFlow. It must be recorded through the authorized stock-entry workflow.","If you physically hold five units when starting, record those once as opening stock. Do not also receive the same five as a supplier delivery.",["Opening stock","Opening quantity","Starting stock"]),
        new("purchase-price","Purchase price","The recorded buying cost for a product or variant. Check whether the current screen treats tax as included or separate.","Copy the appropriate cost from the supplier bill and review tax settings before saving.",["Purchase price","Cost price","Buying price","Unit cost"]),
        new("selling-price","Selling price","The price charged when selling a product or variant, subject to the shop's tax and discount policies.","Review the exact variant's saved price. Price overrides and discounts require the appropriate permissions.",["Selling price","Sale price","Sales price","Unit price"]),
        new("gst","GST registration","The saved tax registration associated with the legal business and mapped shop. The application also supports its configured unregistered-business workflow.","Ask an authorized administrator to review missing shop registration setup; StockBot does not determine tax obligations.",["GST registration","GSTIN","GST number","Tax registration"]),
        new("stock-location","Stock location","The physical shop, warehouse or stock room in which inventory is tracked. Access follows the signed-in user's assigned scope.","Check the active location before receiving or selling. Another shop's available goods do not automatically become available in this shop.",["Stock location","Location","Assigned Shop","Assigned shop","Receiving location"]),
        new("cash","Cash in hand","The cash balance recorded in the business; it is different from UPI/card receipts, sales revenue and a physical cash count.","Use the cash records and daily closing screen to reconcile the drawer; do not treat total sales as cash in the drawer.",["Cash in hand","Opening cash","Cash balance"]),
        new("payment","Payment method","The method by which money is recorded as received. Current New Sale requires full payment using one method.","Choose UPI when the customer pays the full total by UPI. Older credit invoices and authorized collections use their separate workflows.",["Payment method","Payment mode","Payment amount"]),
        new("role","Role","The saved set of permitted screens and actions assigned to this staff account. The role does not grant access to an unassigned shop.","Review Access Preview before creating the account. Role-linked staff follow later changes to the saved role.",["Role","Select Role Template"]),
        new("username","Username","The unique sign-in name for the account, which can differ from the staff member's full name.","Review the suggested username and change it if required. Never include a password in a question to StockBot.",["Username"])
    ];
    public static readonly StockBotError[] Errors = [
        new("INSUFFICIENT_STOCK","Not enough stock","The selected quantity is not currently available. Recheck the exact variant and authorized location, reduce the quantity, or review a recorded stock discrepancy. No available quantity has been guessed."),
        new("INVALID_PAYMENT_AMOUNT","Review the payment","The payment amount must match the permitted amount for this transaction. For current New Sale, pay the full total using one supported method."),
        new("DUPLICATE_RECORD","Record already exists","A unique identifier is already used. Search the existing records before choosing a different code. Do not blindly repeat a save."),
        new("GST_SETUP_INCOMPLETE","Review tax setup","Ask authorized staff to complete the required GST or unregistered-business setup for the current shop before billing."),
        new("PERMISSION_DENIED","Action not permitted","Your current account or shop scope does not allow this action. Ask your Shop Admin to review the assigned role and shop access."),
        new("DATABASE_UNAVAILABLE","Database unavailable","StockFlow cannot currently reach its database. Retry shortly or contact your administrator. For a transaction with an unconfirmed result, check its saved status before submitting again."),
        new("VALIDATION_ERROR","Review the form","Check highlighted required fields and allowed formats. Use Check This Form for local field guidance. StockBot does not fill or submit the form."),
        new("AUTH_REQUIRED","Sign-in required","Sign in again before continuing. Never share your password or authentication code with StockBot."),
        new("CONFLICT","The record changed","Refresh and review the latest record before retrying. Check whether an earlier save already succeeded."),
        new("RATE_LIMITED","Please wait","Wait briefly before retrying the request. Do not repeatedly submit the same transaction."),
        new("NOT_FOUND","Record unavailable","The record could not be found within your current access. Refresh the list or ask an authorized administrator."),
        new("SERVICE_UNAVAILABLE","Service temporarily unavailable","Retry shortly or contact your administrator with the application's request ID. Check an unconfirmed transaction before repeating it.")
    ];
    public static string Resolve(string page, string? mode = null) => page switch
    {
        "/Products/Index" when mode is "create" => "inventory.products.create",
        _ => Entries.FirstOrDefault(entry => entry.Page.Equals(page, StringComparison.OrdinalIgnoreCase))?.Guide.Id ??
            "screen." + Regex.Replace(page.Trim('/').Replace('/', '.').ToLowerInvariant(), "[^a-z0-9.-]", "")
    };
    public StockBotGuide? Guide(string? id)
    {
        var entry = Entries.FirstOrDefault(entry => entry.Guide.Id == id);
        if (entry is null || !access.Can(entry.Module, entry.Action) || !access.CanVisit(entry.Guide.Route))
            return null;
        if (id == "inventory.products.create" && !access.Can("PRODUCT_VARIANTS", "CREATE"))
            return null;
        return entry.Guide;
    }
    public object Metadata(string? id)
    {
        // Filter once on the server. Browser controls receive only the guides
        // that this signed-in user may open, including their required actions.
        var currentGuide = Guide(id);
        var permittedGuides = Entries.Select(entry => Guide(entry.Guide.Id))
            .OfType<StockBotGuide>().ToArray();
        var fields = currentGuide is null ? [] : Fields.Where(field => currentGuide.FieldIds.Contains(field.Id)).ToArray();
        var features = permittedGuides.DistinctBy(guide => guide.Route)
            .Select(guide => new HelpLink(guide.Title, guide.Route)).ToArray();
        var workflows = permittedGuides.Where(guide => guide.Id is
            "sales.new" or "inventory.products.create" or "purchases.new" or "settings.users.create").ToArray();

        // This key remembers only whether the introduction was dismissed.
        var introductionIdentity = $"{scope.TenantId}:{scope.UserId}:{scope.BusinessProfileCode}";
        var identityBytes = System.Text.Encoding.UTF8.GetBytes(introductionIdentity);
        var introductionKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(identityBytes));
        var example = scope.BusinessProfileCode switch
        {
            BusinessProfiles.Footwear => "Footwear example: check the exact size and colour variant before receiving, selling or exchanging it.",
            BusinessProfiles.Clothing => "Garments example: size and colour variants have their own SKU and stock. Review the exact option before selling or exchanging it.",
            BusinessProfiles.BikeAccessories => "Bike accessories example: use the saved part number and compatible-model attributes. Do not assume that a part fits an unlisted model.",
            _ => "Electrical example: where supported, check the exact model and serial unit against the saved warranty information."
        };

        return new
        {
            screen = currentGuide,
            fields,
            introKey = introductionKey,
            features,
            workflows,
            example,
            errors = Errors
        };
    }
    public HelpAnswer? Answer(string question, string? screenId)
    {
        var text = Regex.Replace(question.ToLowerInvariant().Trim().TrimEnd('?', '.', '!'), @"\s+", " ");
        if (text is "explain this screen" or "what is this screen" or "help with this screen" or "guide me step by step")
        {
            var guide = Guide(screenId);
            return guide is null ? new("restricted", "No approved walkthrough is available for this screen within your permissions. Use Browse help articles.", [], [])
                : new HelpAnswer("answered", guide.Purpose, [], []) { Guide = guide };
        }
        var field = Fields.FirstOrDefault(item => text == "what is " + item.Title.ToLowerInvariant() || text == "explain " + item.Title.ToLowerInvariant());
        if (field is not null && Guide(screenId)?.FieldIds.Contains(field.Id) == true)
            return new HelpAnswer("answered", field.Explanation, [], []) { Facts = [new("Retail example", field.Example)] };
        var task = text switch
        {
            "how do i add a product" or "how do i add my first product" or "help me add a product" => ("inventory.products.create", "products"),
            "how do i create my first bill" or "how do i make a bill" or "how do i create a bill" => ("sales.new", "billing"),
            "how do i receive stock" or "guide me through receiving stock" => ("purchases.new", "receiving-stock"),
            _ => ((string, string)?)null
        };
        if (task is { } workflow)
        {
            var guide = Guide(workflow.Item1);
            if (guide is null)
                return new("restricted", "This walkthrough requires permissions your account does not currently have. Contact your Shop Admin.", [], []);
            // Keep approved source excerpts for existing clients; the new drawer
            // renders the stepper instead of repeating a long article.
            var sources = HelpAiService.Sections(access).Where(section => section.ArticleId == workflow.Item2).Take(1).ToArray();
            return new HelpAnswer("answered", guide.Purpose, sources, [new("Open " + guide.Title, guide.Route)]) { Guide = guide };
        }
        if (!Regex.IsMatch(text, @"^(where|find|open|take me to|how (?:do|can) i (?:find|open|register|generate))\b"))
            return null;
        // Preserve detailed workflow questions instead of routing on an incidental
        // word such as "invoice" inside a warranty or returns question.
        if (Regex.IsMatch(text, @"\bwarranty|\breturns?\b|\brefund|\bexchange|\brepairs?\b"))
            return null;
        var id = Regex.IsMatch(text, @"expenses?") ? "expenses.list" : Regex.IsMatch(text, @"employee|staff|new user") ? "settings.users" :
            Regex.IsMatch(text, @"barcode|labels?") ? "barcodes.manage" : Regex.IsMatch(text, @"gst|tax settings") ? "settings.gst" :
            Regex.IsMatch(text, @"sales?|bills?|invoices?") ? "sales.history" : Regex.IsMatch(text, @"products?") ? "inventory.products" : null;
        if (id is null)
            return null;
        var feature = Guide(id);
        return feature is null ? new("restricted", "This feature requires permission that your account does not currently have. Contact your Shop Admin.", [], [])
            : new("answered", feature.Purpose, [], [new("Open " + feature.Title, feature.Route)]);
    }
}
