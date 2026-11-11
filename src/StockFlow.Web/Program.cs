using StockFlow.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StockFlow.Web.Security;

if (args.Contains("--hash-password"))
{
    StockFlow.Web.Security.PasswordHashCommand.Run();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IEWayRepository, EWayRepository>();
builder.Services.AddSingleton<IEWayProvider, UnavailableEWayProvider>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("eway-preparation", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("TenantId")?.Value + ":" + context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Temporary request rate limit. Retry shortly; no monthly StockFlow quota applies." }, cancellationToken);
    };
});
builder.Services.AddScoped<StockFlow.Web.Notifications.NotificationTestService>();
// Register the classes ASP.NET will supply to controllers and services.
builder.Services.AddSingleton<StockFlow.Web.ErrorHandling.ErrorLogService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<DevelopmentLoginLifetime>();
builder.Services.AddOptions<StockFlow.Web.Help.HelpAiOptions>().Bind(builder.Configuration.GetSection("HelpAi"))
    .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && options.TimeoutSeconds is >= 5 and <= 120 &&
        (!options.Enabled || !string.IsNullOrWhiteSpace(options.Model)), "Invalid HelpAi endpoint, model or timeout.").ValidateOnStart();
builder.Services.AddHttpClient<StockFlow.Web.Help.IHelpAiProvider, StockFlow.Web.Help.OllamaHelpProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<StockFlow.Web.Help.HelpAiService>();
builder.Services.AddSingleton<StockFlow.Web.Help.HelpTelemetry>();
builder.Services.AddScoped<StockFlow.Web.Help.HelpTelemetryFilter>();
builder.Services.AddSingleton<StockFlow.Web.Help.HelpModelGate>();
builder.Services.AddHostedService<StockFlow.Web.Help.LocalAiWorker>();
builder.Services.AddHttpClient<StockFlow.Web.Help.HelpVoiceService>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<StockFlow.Web.Help.LocalVoiceWorker>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<StockFlow.Application.Common.ShopTimeOptions>().Bind(builder.Configuration.GetSection("ShopTime"))
    .Validate(options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _), "Choose a valid ShopTime:TimeZoneId.").ValidateOnStart();
builder.Services.AddOptions<SessionSecurityOptions>().Bind(builder.Configuration.GetSection("SessionSecurity"))
.Validate(o => o.IdleMinutes is >= 0 and <= 120 && o.AbsoluteHours is >= 1 and <= 24 && o.IdleMinutes <= o.AbsoluteHours * 60 && o.LoginAttemptsPerIp is >= 1 and <= 10000 && o.LoginWindowMinutes is >= 1 and <= 60 && o.GlobalLoginAttemptsPerMinute is >= 1 and <= 10000,
"Invalid session security limits.")
.ValidateOnStart();
builder.Services.AddScoped<ISharedSessionStore, PostgresSessionStore>();
builder.Services.AddScoped<IdleSessionService>();
builder.Services.AddHostedService<SessionExpirationWorker>();
builder.Services.AddSingleton<SharedProtectionKeys>();
builder.Services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>, ConfigureSharedProtectionKeys>();
var protection = builder.Services.AddDataProtection().SetApplicationName("StockFlow");
var trustedProxies = builder.Configuration.GetSection("SessionSecurity:TrustedProxies").Get<string[]>() ?? [];
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var address in trustedProxies)
        {
            options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
        }
    });
}

var certificatePath = builder.Configuration["SessionSecurity:KeyCertificatePath"];
if (!string.IsNullOrWhiteSpace(certificatePath))
{
    protection.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certificatePath, builder.Configuration["SessionSecurity:KeyCertificatePassword"]));
}

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/ForgotPassword");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddControllers(options =>
{
    options.Filters.Add<StockFlow.Web.Filters.ListQueryValidationFilter>();
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<StockFlow.Web.Filters.MasterDataExceptionFilter>();
    options.Filters.Add<StockFlow.Web.ErrorHandling.ErrorLogPageFilter>();
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<StockFlow.Application.Common.BusinessScope>();
builder.Services.AddScoped<DbConnectionFactory>();
builder.Services.AddScoped<IBranchRepository, BranchRepository>();
builder.Services.AddScoped<IBusinessProfileRepository, BusinessProfileRepository>();
builder.Services.AddScoped<ICatalogSchemaRepository, CatalogSchemaRepository>();
builder.Services.AddScoped<RetailVariantSelection>();
builder.Services.AddScoped<ITransferRepository, TransferRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IBrandRepository, BrandRepository>();
builder.Services.AddScoped<IBrandService, BrandService>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IVariantRepository, VariantRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
// Repositories read and write data; services validate the business rules.
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IPurchaseRepository, PurchaseRepository>();
builder.Services.AddScoped<IPurchaseChangeRepository, PurchaseRepository>();
builder.Services.AddScoped<IPurchaseReceiptRepository, PurchaseReceiptRepository>();
builder.Services.AddScoped<IStockIntakeRepository, StockIntakeRepository>();
builder.Services.AddScoped<IBarcodeLotRepository, BarcodeLotRepository>();
builder.Services.AddScoped<IBarcodeManagementRepository, BarcodeManagementRepository>();
builder.Services.AddScoped<IPurchaseService, PurchaseService>();
builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
builder.Services.AddScoped<IReportingRepository, ReportingRepository>();
builder.Services.AddScoped<ICashClosingRepository, CashClosingRepository>();
builder.Services.AddScoped<ICashInHandRepository, CashInHandRepository>();
builder.Services.Configure<StockFlow.Application.DTOs.Operations.BankAccountsOptions>(builder.Configuration.GetSection("BankAccounts"));
builder.Services.AddScoped<IBankAccountsRepository,BankAccountsRepository>();
builder.Services.AddScoped<BankAccountProtection>();
builder.Services.AddScoped<IExpenseRepository, ExpenseRepository>();
builder.Services.AddScoped<IShopMasterRepository, ShopMasterRepository>();
builder.Services.AddScoped<IScreenLanguageRepository, ScreenLanguageRepository>();
builder.Services.AddScoped<IOperationsRepository, OperationsRepository>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IReplenishmentRepository, ReplenishmentRepository>();
builder.Services.AddScoped<IStockReceiptHistoryRepository, StockReceiptHistoryRepository>();
builder.Services.AddScoped<ISalesRepository, SalesRepository>();
builder.Services.AddScoped<ISalesEnquiryRepository, SalesEnquiryRepository>();
builder.Services.AddScoped<IHelpBusinessRepository, HelpBusinessRepository>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IWarrantyRepository, WarrantyRepository>();
builder.Services.AddScoped<IWarrantyService, WarrantyService>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<IServiceService, ServiceService>();
builder.Services.AddScoped<ICompanySettingsRepository, CompanySettingsRepository>();
builder.Services.AddScoped<IGstRepository, GstRepository>();
builder.Services.AddScoped<ICrmRepository, CrmRepository>();
builder.Services.AddScoped<IQuotationRepository, QuotationRepository>();
builder.Services.AddScoped<IScanRepository, ScanRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddHttpClient<StockFlow.Web.Notifications.INotificationEmailProvider,StockFlow.Web.Notifications.BrevoNotificationEmailProvider>(client=>client.Timeout=TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false})
    .RedactLoggedHeaders(new[]{"api-key"});
builder.Services.AddHostedService<StockFlow.Web.Notifications.NotificationWorker>();
builder.Services.Configure<StockFlow.Web.Notifications.WhatsAppOptions>(builder.Configuration.GetSection("Notifications:WhatsApp"));
builder.Services.AddSingleton<StockFlow.Web.Notifications.WhatsAppTokenProtector>();
builder.Services.AddSingleton<StockFlow.Web.Notifications.WhatsAppWorkerStore>();
builder.Services.AddScoped<StockFlow.Web.Notifications.WhatsAppService>();
builder.Services.AddHttpClient<StockFlow.Web.Notifications.MetaWhatsAppClient>(client=>client.Timeout=TimeSpan.FromSeconds(20))
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false})
    .RemoveAllLoggers();
builder.Services.AddHostedService<StockFlow.Web.Notifications.WhatsAppWorker>();
builder.Services.AddScoped<IScanService, ScanService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ISecurityRepository, SecurityRepository>();
builder.Services.AddScoped<StockFlow.Web.Security.PermissionContext>();
builder.Services.AddScoped<StockFlow.Web.Security.TwoFactorService>();
builder.Services.AddScoped<IAuthService, SecurityAuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "StockFlow.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.SlidingExpiration = false;
    var idleMinutes = builder.Configuration.GetValue("SessionSecurity:IdleMinutes", 0);
    options.ExpireTimeSpan = idleMinutes == 0
        ? TimeSpan.FromHours(builder.Configuration.GetValue("SessionSecurity:AbsoluteHours", 8))
        : TimeSpan.FromMinutes(idleMinutes);
    options.Events.OnSigningIn = async context =>
    {
        context.HttpContext.RequestServices.GetRequiredService<DevelopmentLoginLifetime>().Stamp(context.Properties);
        var identity = (System.Security.Claims.ClaimsIdentity)context.Principal!.Identity!;
        var sessions = context.HttpContext.RequestServices.GetRequiredService<IdleSessionService>();
        SessionStatus status;
        if (!identity.HasClaim(c => c.Type == StockFlow.Web.Security.IdleSessionService.ClaimType))
        {
            var started = await sessions.StartAsync(context.Principal, context.HttpContext);
            identity.AddClaim(new System.Security.Claims.Claim(IdleSessionService.ClaimType, started.Id));
            status = started.Status;
        }
        else
        {
            status = await sessions.ValidateAsync(context.Principal);
        }

        if (!status.Active)
        {
            throw new AuthenticationFailureException("This login session has ended.");
        }

        context.HttpContext.Items["StockFlow.SessionStatus"] = status;
        var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        context.Properties.ExpiresUtc = clock.GetUtcNow().AddMilliseconds(status.RemainingMilliseconds);
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<DevelopmentLoginLifetime>().Accepts(context.Properties))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        var sessions = context.HttpContext.RequestServices.GetRequiredService<StockFlow.Web.Security.IdleSessionService>();
        SessionStatus status;
        try
        {
            status = await sessions.ValidateAsync(context.Principal!);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or TimeoutException)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<IdleSessionService>>().LogError(error, "Shared session validation unavailable.");
            context.HttpContext.Items["StockFlow.SessionUnavailable"] = true;
            context.RejectPrincipal();
            return;
        }

        context.HttpContext.Items["StockFlow.SessionStatus"] = status;
        if (status.Active)
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    };
    options.Events.OnSigningOut = async context =>
    {
        await context.HttpContext.RequestServices.GetRequiredService<IdleSessionService>().EndAsync(context.HttpContext.User, context.HttpContext.Items["StockFlow.SessionEndReason"]?.ToString() ?? "SECURITY_CHANGED");
    };
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.HttpContext.Items["StockFlow.SessionUnavailable"] is true)
        {
            context.Response.StatusCode = 503;
            return context.Response.WriteAsync("Sign-in services are temporarily unavailable. Please try again.");
        }

        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/account/session"))
        {
            context.Response.StatusCode = 401;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 403;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
var app = builder.Build();
if (trustedProxies.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler("/error-log");
app.UseMiddleware<StockFlow.Web.ErrorHandling.ErrorLogMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "StockFlow API v1");
        // Swagger embeds this JSON inside a JavaScript string. Literal line breaks
        // are decoded before JSON.parse, so keep the interceptor on one line.
        options.UseRequestInterceptor("""
            function (request) {
                if (['GET', 'HEAD', 'OPTIONS'].includes((request.method || 'GET').toUpperCase())) {
                    return request;
                }
                return fetch('/api/antiforgery', { credentials: 'same-origin' })
                    .then(function (response) {
                        if (!response.ok) throw new Error('Sign in to StockFlow before testing API changes.');
                        return response.json();
                    })
                    .then(function (token) {
                        request.headers = request.headers || {};
                        request.headers['X-CSRF-TOKEN'] = token.requestToken;
                        return request;
                    });
            }
            """.ReplaceLineEndings(" "));
    });
    app.MapGet("/api/antiforgery",
(Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, HttpContext context) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            requestToken = antiforgery.GetAndStoreTokens(context).RequestToken
        });
    })
.RequireAuthorization()
.ExcludeFromDescription();
    if (builder.Configuration.GetValue<bool>("StockFlow:OpenBrowserTabs"))
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var address = app.Urls.FirstOrDefault(url => url.StartsWith("http://localhost:"));
            if (address is null)
            {
                return;
            }

            foreach (var path in new[]
            {
                "/",
                "/swagger"
            }

            )
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address + path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    app.Logger.LogWarning(ex, "Could not open browser for {Path}", path);
                }
            }
        });
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// The local HTTP profile has no HTTPS endpoint to redirect to.
// Keep redirection for production and the Development HTTPS profile.
if (!app.Environment.IsDevelopment() || app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) || builder.Configuration.GetValue<int?>("HTTPS_PORT") is > 0)
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseMiddleware<LoginThrottleMiddleware>();
app.UseAuthentication();
app.UseMiddleware<StockFlow.Web.Security.BusinessScopeMiddleware>();
app.UseMiddleware<StockFlow.Web.Security.PermissionMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
// Serve public assets before tenant and permission middleware can redirect them.
app.MapStaticAssets().AllowAnonymous().ShortCircuit();
app.MapControllers();
app.MapRazorPages().WithStaticAssets();
app.Run();
public partial class Program
{
}
