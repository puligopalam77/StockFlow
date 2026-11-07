using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

public static class Checks
{
    public static async Task Main(string[] args)
    {
        if(args.Contains("--supplier-directory-checks")){using var directory=new Factory();await SupplierDirectoryChecks.Run(directory);return;}
        if(args.Contains("--supplier-address-database-checks")){await SupplierAddressDatabaseChecks.Run();return;}
        if(args.Contains("--customer-address-checks")){using var customers=new Factory();await CustomerAddressChecks.Run(customers);return;}
        if(args.Contains("--expense-modern-checks")){await ExpenseModernChecks.Run();return;}
        if(args.Contains("--company-views-checks")){await CompanyViewsChecks.Run();return;}
        if(args.Contains("--store-management-checks")){await StoreManagementChecks.Run();return;}
        if(args.Contains("--company-settings-database-checks")){await CompanySettingsDatabaseChecks.Run();return;}
        if(args.Contains("--replenishment-checks")){await ReplenishmentChecks.Run();return;}
        if(args.Contains("--low-stock-checks")){await LowStockChecks.Run();return;}
        if(args.Contains("--private-shop-checks")){await PrivateShopExpenseChecks.Run(false);return;}
        if(args.Contains("--expense-checks")){await PrivateShopExpenseChecks.Run(true);return;}
        if(args.Contains("--p0-reconciliation-checks")){await ReliabilityChecks.Run();return;}
        if(args.Contains("--restored-app-checks")){await ReliabilityChecks.RestoredApplication();return;}
        if(args.Contains("--workflow-reporting-checks")){await WorkflowReportingChecks.Run();return;}
        if(args.Contains("--eway-checks")){await EWayChecks.Run();return;}
        if(args.Contains("--operational-readiness-checks")){await OperationalReadinessChecks.Run();return;}
        if(args.Contains("--operational-readiness-database-checks")){await OperationalReadinessChecks.Run(true);return;}
        if(args.Contains("--quotation-checks")){await QuotationChecks.Run();return;}
        if(args.Contains("--gst-spreadsheet-checks")){GstSpreadsheetChecks.Run();return;}
        if(args.Contains("--whatsapp-checks")){await WhatsAppChecks.Run();return;}
        if(args.Contains("--gst-catalog-checks")){await GstCatalogChecks.Run();return;}
        if(args.Contains("--gst-catalog-large-checks")){await GstCatalogChecks.Run(true);return;}
        if(args.Contains("--crm-checks")){await CrmChecks.Run();return;}
        if(args.Contains("--gst-setup-checks")){await GstSetupChecks.Run();return;}
        if(args.Contains("--responsive-audit-server")){await ResponsiveAuditServer.Run();return;}
        if(args.Contains("--list-pagination-database-checks")){await ListPaginationChecks.Database();return;}
        if(args.Contains("--legacy-sku-correction-checks")){await LegacySkuCorrectionChecks.Run();return;}
        if(args.Contains("--product-sku-preview-checks")){await ProductSkuPreviewChecks.Run();return;}
        if(args.Contains("--product-list-sku-checks")){await ProductListSkuChecks.Run();return;}
        if(args.Contains("--product-edit-integrity-checks")){await ProductEditIntegrityChecks.Run();return;}
        if(args.Contains("--receive-stock-integration-checks")){await ReceiveStockIntegrationChecks.Run();return;}
        if(args.Contains("--quick-customer-integration-checks")){await QuickCustomerIntegrationChecks.Run();return;}
        if(args.Contains("--new-sale-integration-checks")){await NewSaleIntegrationChecks.Run();return;}
        if(args.Contains("--fast-billing-checks")){await FastBillingChecks.Run();return;}
        if(args.Contains("--saved-bills-checks")){await SavedBillsChecks.Run();return;}
        if(args.Contains("--draft-numbering-checks")){await DraftNumberingChecks.Run();return;}
        if(args.Contains("--draft-lifecycle-checks")){await DraftLifecycleChecks.Run();return;}
        if(args.Contains("--identifier-integration-database-checks")){await IdentifierIntegrationDatabaseChecks.Run();return;}
        if(args.Contains("--product-descriptive-details-database-checks")){await ProductDescriptiveDetailsDatabaseChecks.Run();return;}
        if(args.Contains("--sequential-sku-database-checks")){await SequentialSkuDatabaseChecks.Run();return;}
        if(args.Contains("--product-identity-database-checks")){await ProductIdentityDatabaseChecks.Run();return;}
        if (args.Contains("--purchase-correction-database-checks")) { await PurchaseCorrectionDatabaseChecks.Run(); return; }
        if (args.Contains("--stock-barcode-workflow-checks")) { await StockBarcodeWorkflowChecks.Run(); return; }
        if (args.Contains("--bike-demo-setup-checks")) { await BikeDemoSetupChecks.Run(); return; }
        if (args.Contains("--catalog-review-checks")) { await CatalogRegistrationReviewChecks.Run(); return; }
        if (args.Contains("--retail-phase56-checks")) { await RetailTransactionChecks.Run(); return; }
        if (args.Contains("--category-attribute-checks")) { await CategoryAttributeChecks.Run(); return; }
        if (args.Contains("--category-registration-tab-checks")) { await CategoryRegistrationTabChecks.Run(); return; }
        if (args.Contains("--brand-registration-checks")) { await BrandRegistrationChecks.Run(); return; }
        if (args.Contains("--electrical-phase1-checks")) { await ElectricalPhaseOneChecks.Run(); return; }
        if (args.Contains("--business-profile-phase2-checks")) { using var profiles = new Factory(); await BusinessProfilePhaseTwoChecks.Run(profiles); return; }
        if (args.Contains("--sender-verification-checks")) { await SenderVerificationChecks.Run(); return; }
        if (args.Contains("--staff-account-database-checks")) { await MultiStoreDatabaseChecks.Run(staffAccountsOnly:true); return; }
        if (args.Contains("--notification-database-checks")) { await NotificationDatabaseChecks.Run(); return; }
        if (args.Contains("--help-intent-live")) { await HelpIntentLiveChecks.Run(); return; }
        if (args.Contains("--help-sales-database-readonly")) { await HelpSalesDatabaseReadChecks.Run(); return; }
        if (args.Contains("--session-worker-checks")) { await SessionWorkerChecks.Run(); return; }
        if (args.Length == 0) await SessionWorkerChecks.Run();
        if (args.Contains("--screen-language-schema-check")) { await ScreenLanguageSchemaChecks.Run(); return; }
        if (args.Contains("--apply-local-screen-language-migrations")) { await ScreenLanguageSchemaChecks.Run(apply:true); return; }
        if (args.Contains("--shop-readiness-database-test")) { await ShopReadinessDatabaseChecks.Run(); return; }
        if (args.Contains("--error-log-checks")) { await ErrorLogChecks.Run(); return; }
        if (args.Contains("--brand-category-database-checks")) { await MultiStoreDatabaseChecks.Run(brandCategoriesOnly:true); return; }
        if (args.Contains("--shared-session-database-checks")) { await MultiStoreDatabaseChecks.Run(sharedSessionsOnly:true); return; }
        if (args.Contains("--billing-database-test")) { await BillingDatabaseChecks.Run(); return; }
        if (args.Contains("--multistore-database-test")) { await MultiStoreDatabaseChecks.Run(args.Contains("--baseline-through-025")); return; }
        if (args.Contains("--receiving-database-test")) { await ReceivingDatabaseChecks.Run(); return; }
        if (args.Contains("--variant-database-test")) { await VariantDatabaseChecks.Run(); return; }
        if (args.Contains("--sales-database-test")) { await SalesDatabaseChecks.Run(); return; }
        if (args.Contains("--database-readonly")) { await DatabaseReadChecks.Run(); return; }
        if (args.Contains("--product-sales-readonly")) { await ProductSalesReadChecks.Run(); return; }
        if (args.Contains("--purchase-database-test")) { await PurchaseDatabaseChecks.Run(); return; }
        if (args.Contains("--receiving-catalog-extension-checks")) { await ReceivingCatalogExtensionChecks.Run(); return; }
        if (args.Contains("--stock-receipt-database-read")) { await StockReceiptDatabaseReadChecks.Run(); return; }
        using var factory = new Factory();
        if(args.Contains("--crm-http-checks")){await CrmHttpChecks.Run(factory);return;}
        if(args.Contains("--gst-http-checks")){await GstHttpChecks.Run(factory);return;}
        if(args.Contains("--gst-checks")){await GstChecks.Run();return;}
        if(args.Contains("--gst-template-checks")){await GstChecks.Run(true);return;}
        if(args.Contains("--stock-receipt-pagination-checks")){await StockReceiptPaginationChecks.Run(factory);return;}
        if(args.Contains("--list-pagination-checks")){await ListPaginationChecks.Run(factory);return;}
        if(args.Contains("--receive-stock-ui-checks")){await ReceiveStockChecks.Run(factory);return;}
        if(args.Contains("--product-registration-core-checks")){await ProductRegistrationChecks.Run(factory);return;}
        if(args.Contains("--master-screen-checks")){await MasterScreenChecks.Run(factory);return;}
        if(args.Contains("--product-review-checks")){await ProductReviewChecks.Run(factory);return;}
        if(args.Contains("--identifier-integration-checks")){await ScanChecks.Run(factory);await WarrantyChecks.Run(factory);await ReceivingChecks.Run(factory);await InvoiceChecks.Run(factory);return;}
        if(args.Contains("--purchase-correction-checks")){await PurchaseCorrectionChecks.Run(factory);return;}
        if(args.Contains("--barcode-management-checks")){await BarcodeManagementChecks.Run(factory);return;}
        if (args.Contains("--notification-checks")) { await NotificationChecks.Run(factory); return; }
        if (args.Contains("--configuration-ui-fixtures")) { await ConfigurationChecks.Run(factory); return; }
        if (args.Contains("--help-voice-checks") || args.Contains("--help-voice-live")) { await HelpVoiceChecks.Run(factory,args.Contains("--help-voice-live")); return; }
        if (args.Contains("--list-screen-fixtures")) { await ListScreenFixtures.Run(factory); return; }
        if (args.Contains("--sales-list-checks")) { await SalesChecks.Run(factory); await ListScreenFixtures.Run(factory); return; }
        if (args.Contains("--company-profile-checks")) { await CompanyProfileChecks.Run(factory); return; }
        if (args.Contains("--dashboard-ui-fixtures")) { await DashboardChecks.Fixtures(factory); return; }
        if (args.Contains("--dashboard-checks")) { await DashboardChecks.Run(factory); return; }
        if (args.Contains("--help-stock-checks")) { await HelpStockChecks.Run(factory); return; }
        if (args.Contains("--help-sales-checks")) { await HelpSalesChecks.Run(factory); return; }
        if (args.Contains("--help-business-checks")) { await HelpBusinessChecks.Run(factory); return; }
        if (args.Contains("--help-ai-checks") || args.Contains("--help-ai-live")) { await HelpAiChecks.Run(factory, args.Contains("--help-ai-live")); return; }
        if (args.Contains("--help-panel-checks")) { await HelpPanelChecks.Run(factory); return; }
        if (args.Contains("--login-validation-checks")) { await ForgotPasswordChecks.Run(factory); await LoginValidationChecks.Run(factory); return; }
        if (args.Contains("--forgot-password-checks")) { await ForgotPasswordChecks.Run(factory); return; }
        if (args.Contains("--screen-language-checks")) { await ScreenLanguageChecks.Run(factory); return; }
        if (args.Length == 0) await ScreenLanguageChecks.Run(factory);
        if (args.Contains("--shop-readiness-checks")) { await ShopReadinessChecks.Run(factory); return; }
        if (args.Length == 0) await ShopReadinessChecks.Run(factory);
        if (args.Contains("--staff-account-checks")) { await SecurityChecks.Run(factory); await SecurityChecks.Fixtures(factory); return; }
        if (args.Contains("--field-validation-checks")) { await FieldValidationChecks.Run(factory); return; }
        if (args.Length == 0) await FieldValidationChecks.Run(factory);
        if(args.Contains("--invoice-checks")){await InvoiceChecks.Run(factory);return;}
        if(args.Contains("--idle-session-checks")){await IdleSessionChecks.Run(factory);return;}
        if(args.Contains("--brand-category-checks")){await BrandCategoryChecks.Run(factory);await ConfigurationChecks.Run(factory);await VariantUiFixtures.Write(factory);return;}
        if(args.Contains("--purchase-receiving-checks")){await PurchaseReceivingChecks.Run(factory);await VariantUiFixtures.Write(factory);return;}
        if(args.Length==0)await PurchaseReceivingChecks.Run(factory);
        if(args.Contains("--inventory-control-checks")){await InventoryControlChecks.Run(factory);return;}
        if(args.Length==0)await InventoryControlChecks.Run(factory);
        if(args.Contains("--quick-customer-checks")){await QuickCustomerChecks.Run(factory);return;}
        if(args.Contains("--billing-checks")){await BillingChecks.Run(factory);return;}
        if(args.Length==0)await BillingChecks.Run(factory);
        if(args.Contains("--label-checks")){await LabelChecks.Run(factory);return;}
        if(args.Contains("--category-hierarchy-checks")){await CatalogRegistrationChecks.Run(factory);await CategoryHierarchyChecks.Run(factory);return;}
        if(args.Contains("--two-level-category-checks")){await TwoLevelCategoryChecks.Run(factory);return;}
        if(args.Length==0)await LabelChecks.Run(factory);
        if(args.Contains("--receiving-checks")){await ReceivingChecks.Run(factory);return;}
        if(args.Length==0)await ReceivingChecks.Run(factory);
        if(args.Contains("--navigation-checks")){await NavigationChecks.Run(factory);await ConfigurationChecks.Run(factory);return;}
        if(args.Contains("--store-assignment-checks")){await StoreAssignmentChecks.Run(factory);return;}
        if(args.Contains("--reports-checks")){await ReportsChecks.Run(factory);return;}
        if (args.Contains("--supplier-checks")) { await SupplierRegistrationChecks.Run(factory); await ConfigurationChecks.Run(factory); await VariantUiFixtures.Write(factory); return; }
        await StoreAssignmentChecks.Run(factory);
        if(args.Contains("--admin-action-checks")){await AdminActionChecks.Run(factory);await SecurityChecks.Run(factory);await SecurityChecks.Fixtures(factory);return;}
        if (args.Contains("--catalog-checks")) { await CatalogRegistrationChecks.Run(factory); await ConfigurationChecks.Run(factory); return; }
        if (args.Contains("--registration-checks")) { await ProductRegistrationChecks.Run(factory); await ConfigurationChecks.Run(factory); return; }
        if (args.Contains("--variant-ui-fixtures")) { await VariantUiFixtures.Write(factory); return; }
        await SecurityChecks.Run(factory);
        if(args.Contains("--security-ui-fixtures")){await SecurityChecks.Fixtures(factory);return;}
        await DashboardChecks.Run(factory);
        if (args.Contains("--service-ui-fixtures")) { await ServiceUiFixtures.Write(factory); return; }
        await ServiceChecks.Run(factory);
        await WarrantyChecks.Run(factory);
        await InvoiceChecks.Run(factory);
        await SalesChecks.Run(factory);
        using var anonymous = factory.CreateClient();
        await ScanChecks.Run(factory);
        foreach (var module in new[] { "categories", "brands", "suppliers", "customers", "products" })
            await Expect(anonymous.GetAsync("/api/" + module), 401, "anonymous " + module);
        using var admin = await Client(factory, "Admin");
        await Expect(anonymous.GetAsync("/api/inventory"), 401, "anonymous inventory rejected");
        await Expect(admin.GetAsync("/Inventory"), 200, "inventory page renders");
        await Expect(admin.GetAsync("/api/inventory/history?from=2026-10-01&to=2026-09-01"), 400, "reversed history dates rejected");
        await Expect(admin.GetAsync("/api/inventory/units?offset=-1"), 400, "negative unit offset rejected");
        await Expect(admin.PostAsJsonAsync("/api/inventory/adjustments", new { productId = 1, quantity = 1 }), 400, "invalid adjustment rejected before database access");
        foreach (var role in new[] { "Sales", "Service" })
        {
            using var inventoryReader = await Client(factory, role);
            await Expect(inventoryReader.GetAsync("/Inventory"), 200, role + " inventory page access");
            await Expect(inventoryReader.PostAsJsonAsync("/api/inventory/adjustments", new { }), 403, role + " stock adjustment denied");
        }
        var swagger = await anonymous.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json");
        Check(swagger!["paths"]!["/api/purchases"] is not null && swagger["paths"]!["/api/purchases/{id}/complete"] is not null, "Swagger includes purchase APIs");
        var swaggerScript = await anonymous.GetStringAsync("/swagger/index.js");
        Check(!swaggerScript.Contains("async (request)") && swaggerScript.Contains("X-CSRF-TOKEN"), "Swagger interceptor uses parser-compatible function");
        await Expect(anonymous.GetAsync("/api/purchases"), 401, "anonymous purchase access rejected");
        foreach (var page in new[] { "/Purchases", "/Purchases/Create", "/Purchases/Details/1" })
            await Expect(admin.GetAsync(page), 200, "purchase page " + page);
        await Expect(admin.PostAsJsonAsync("/api/purchases", new { supplierId = 1, purchaseDate = "2026-09-28", items = Array.Empty<object>() }), 400, "empty purchase rejected before database call");
        foreach (var page in new[] { "Categories", "Brands", "Suppliers", "Customers", "Products" })
        {
            var response = await admin.GetAsync("/" + page);
            var html = await response.Content.ReadAsStringAsync();
            Check(response.IsSuccessStatusCode && html.Contains("master-data") && html.Contains("csrf-token"), page + " page renders");
        }
        var category = await Create(admin, "categories", new { categoryName = "Battery" });
        await Expect(admin.PostAsJsonAsync("/api/categories", new { categoryName = " battery " }), 400, "case-insensitive category duplicate");
        var brand = await Create(admin, "brands", new { brandName = "Exide", categoryIds = new[]{category["categoryId"]!.GetValue<int>()} });
        await Expect(admin.PostAsJsonAsync("/api/brands", new { brandName = "EXIDE" }), 400, "case-insensitive brand duplicate");
        int categoryId = category["categoryId"]!.GetValue<int>();
        int brandId = brand["brandId"]!.GetValue<int>();
        var product = new JsonObject { ["productName"] = "Battery Model", ["categoryId"] = categoryId, ["brandId"] = brandId, ["sku"] = "SKU-1", ["barcode"] = "8900001", ["purchasePrice"] = 4500.25m, ["sellingPrice"] = 5200.50m, ["taxPercentage"] = 18m, ["warrantyMonths"] = 48, ["minimumStockLevel"] = 5, ["isSerialTracked"] = true };
        var created = await Create(admin, "products", product);
        int productId = created["productId"]!.GetValue<int>();
        Check(created["sellingPrice"]!.GetValue<decimal>() == 5200.50m && created["isSerialTracked"]!.GetValue<bool>() && created["warrantyMonths"]!.GetValue<int>() == 48, "product fields round trip");
        await Expect(admin.PostAsJsonAsync("/api/products", product), 400, "duplicate SKU");
        product["sku"] = "SKU-2";
        await Expect(admin.PostAsJsonAsync("/api/products", product), 400, "duplicate barcode");
        product["barcode"] = "8900002";
        product["categoryId"] = 0;
        await Expect(admin.PostAsJsonAsync("/api/products", product), 400, "category ID validation");
        product["categoryId"] = categoryId;
        product["sellingPrice"] = -1;
        await Expect(admin.PostAsJsonAsync("/api/products", product), 400, "negative price validation");
        product["sellingPrice"] = 5300;
        product["isActive"] = true;
        await Expect(admin.PutAsJsonAsync("/api/products/" + productId, product), 204, "product edit excludes own SKU/barcode");
        await Expect(admin.PutAsJsonAsync("/api/products/" + productId, product), 204, "unchanged SKU/barcode accepted on update");
        await Expect(admin.GetAsync("/api/products/barcode/8900002"), 200, "barcode lookup");
        await Expect(admin.DeleteAsync("/api/products/" + productId), 204, "product deactivate");
        await Expect(admin.GetAsync("/api/products/barcode/8900002"), 404, "inactive barcode excluded");
        var active = await admin.GetFromJsonAsync<JsonArray>("/api/products?activeOnly=true");
        Check(active!.Count == 0, "active product filter");
        await Expect(admin.GetAsync("/api/products/" + productId), 200, "soft deletion retains record");
        await Expect(admin.DeleteAsync("/api/categories/" + categoryId), 204, "category deactivate");
        await Expect(admin.PostAsJsonAsync("/api/products", product), 400, "inactive category rejected");
        foreach (var module in new[] { "suppliers", "customers" })
        {
            string singular = module[..^1];
            var body = new JsonObject { [singular + "Name"] = "Test Name", ["mobileNumber"] = "9876543210" };
            var entity = await Create(admin, module, body);
            var id = entity[singular + "Id"]!.GetValue<int>();
            var matches = await admin.GetFromJsonAsync<JsonArray>("/api/" + module + "/search?term=9876");
            Check(matches!.Count == 1, module + " mobile search");
            body[singular + "Name"] = "Edited Name"; body["isActive"] = true;
            await Expect(admin.PutAsJsonAsync("/api/" + module + "/" + id, body), 204, module + " edit");
            await Expect(admin.DeleteAsync("/api/" + module + "/" + id), 204, module + " deactivate");
        }
        using var sales = await Client(factory, "Sales");
        using var service = await Client(factory, "Service");
        await Expect(sales.GetAsync("/api/purchases"), 403, "Sales purchase access rejected");
        await Expect(service.PostAsJsonAsync("/api/purchases", new { }), 403, "Service purchase creation rejected");
        await Expect(sales.GetAsync("/api/suppliers"), 403, "Sales supplier access blocked");
        await Expect(service.GetAsync("/api/products"), 200, "Service product read");
        await Expect(sales.PostAsJsonAsync("/api/products", product), 403, "Sales product write blocked");
        await Expect(service.PostAsJsonAsync("/api/customers", new { customerName = "Customer" }), 403, "Service customer write blocked");
        var customer = await Create(sales, "customers", new { customerName = "Sales Customer" });
        await Expect(sales.PutAsJsonAsync("/api/customers/" + customer["customerId"], new { customerName = "Sales Customer", isActive = false }), 403, "Sales cannot bypass deactivate via PUT");
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Expect(admin.PostAsJsonAsync("/api/inventory/adjustments", new { }), 400, "inventory anti-forgery required");
        await Expect(admin.PostAsJsonAsync("/api/brands", new { brandName = "No Token" }), 400, "anti-forgery token required");
        Console.WriteLine("All master-data checks passed. No PostgreSQL writes were performed.");
    }
    private static async Task<HttpClient> Client(Factory factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role", role);
        var token = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!["requestToken"]!.GetValue<string>());
        return client;
    }
    private static async Task<JsonObject> Create(HttpClient client, string module, object value)
    {
        var response = await client.PostAsJsonAsync("/api/" + module, value);
        Check(response.StatusCode == HttpStatusCode.Created, module + " create: " + await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static async Task Expect(Task<HttpResponseMessage> request, int status, string name)
    {
        var response = await request;
        Check((int)response.StatusCode == status, name + " (HTTP " + (int)response.StatusCode + ")");
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
}

sealed class Factory : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        builder.UseContentRoot(Path.Combine(root?.FullName ?? throw new InvalidOperationException("Run tests within the solution."), "src", "StockFlow.Web"));
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddHttpContextAccessor();
            services.RemoveAll<StockFlow.Web.Security.ISharedSessionStore>();
            services.AddSingleton<StockFlow.Web.Security.ISharedSessionStore,FakeSharedSessionStore>();
            services.RemoveAll<IBranchRepository>(); services.AddSingleton<IBranchRepository, FakeBranchRepository>();
            services.RemoveAll<IBusinessProfileRepository>(); services.AddSingleton<IBusinessProfileRepository, FakeBusinessProfileRepository>();
            services.RemoveAll<ICatalogSchemaRepository>(); services.AddSingleton<ICatalogSchemaRepository, FakeCatalogSchemaRepository>();
            services.RemoveAll<ICashClosingRepository>(); services.AddSingleton<ICashClosingRepository, FakeCashClosingRepository>();
            services.RemoveAll<IScreenLanguageRepository>(); services.AddSingleton<IScreenLanguageRepository, FakeScreenLanguageRepository>();
            services.RemoveAll<IStockIntakeRepository>(); services.AddSingleton<IStockIntakeRepository, FakeStockIntakeRepository>();
            services.RemoveAll<ISecurityRepository>(); services.AddScoped<ISecurityRepository, FakeSecurityRepository>();
            services.RemoveAll<IDashboardRepository>(); services.AddSingleton<IDashboardRepository, FakeDashboardRepository>();
            services.RemoveAll<IServiceRepository>(); services.AddSingleton<IServiceRepository, FakeServiceRepository>();
            services.RemoveAll<IWarrantyRepository>(); services.AddSingleton<IWarrantyRepository, FakeWarrantyRepository>();
            services.RemoveAll<IInvoiceRepository>(); services.AddSingleton<IInvoiceRepository, FakeInvoiceRepository>();
            services.RemoveAll<ICompanySettingsRepository>(); services.AddSingleton<ICompanySettingsRepository, FakeCompanySettingsRepository>();
            services.RemoveAll<ISalesRepository>(); services.AddSingleton<ISalesRepository, FakeSalesRepository>();
            services.RemoveAll<IScanRepository>(); services.AddSingleton<IScanRepository, FakeScanRepository>();
            services.RemoveAll<IBarcodeLotRepository>(); services.AddSingleton<IBarcodeLotRepository, FakeBarcodeLotRepository>();
            services.RemoveAll<IBarcodeManagementRepository>(); services.AddSingleton<IBarcodeManagementRepository, FakeBarcodeManagementRepository>();
            services.RemoveAll<ICategoryRepository>(); services.AddSingleton<ICategoryRepository, FakeCategoryRepository>();
            services.RemoveAll<IBrandRepository>(); services.AddSingleton<IBrandRepository, FakeBrandRepository>();
            services.RemoveAll<ISupplierRepository>(); services.AddSingleton<ISupplierRepository, FakeSupplierRepository>();
            services.RemoveAll<ICustomerRepository>(); services.AddSingleton<ICustomerRepository, FakeCustomerRepository>();
            services.RemoveAll<IProductRepository>(); services.AddSingleton<IProductRepository, FakeProductRepository>();
            services.RemoveAll<IVariantRepository>(); services.AddSingleton<IVariantRepository, FakeVariantRepository>();
            services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; options.DefaultForbidScheme = "Test"; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
        });
    }
}
sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["Test-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers["Test-UserId"].FirstOrDefault() ?? "1"), new Claim("SecurityVersion",Request.Headers["Test-Version"].FirstOrDefault() ?? "1"), new Claim(ClaimTypes.Name, role), new Claim(ClaimTypes.Role, role) }, Scheme.Name);
        identity.AddClaims([new("TenantId",Request.Headers["Test-Tenant"].FirstOrDefault() ?? "1"),new("StoreId",Request.Headers["Test-Store"].FirstOrDefault() ?? "1"),new("LocationId",Request.Headers["Test-Location"].FirstOrDefault() ?? "1")]);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
