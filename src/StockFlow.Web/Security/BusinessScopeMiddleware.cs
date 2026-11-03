using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using StockFlow.Application.Common;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Security;

public class BusinessScopeMiddleware
{
    private readonly RequestDelegate next;
    // ASP.NET supplies these services when it creates this class.
    public BusinessScopeMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext http, BusinessScope scope, IBranchRepository branches)
    {
        if (http.Request.Path.Equals(new PathString("/error-log")))
        {
            await next(http);
            return;
        }

        if (http.User.Identity?.IsAuthenticated != true)
        {
            await next(http);
            return;
        }

        if (!long.TryParse(http.User.FindFirstValue("TenantId"), out var tenant) || tenant <= 0 || !int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor <= 0)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (http.Request.Path.StartsWithSegments("/api"))
            {
                http.Response.StatusCode = 401;
                return;
            }

            http.Response.Redirect("/Account/Login");
            return;
        }

        scope.TenantId = tenant;
        scope.UserId = actor;
        var state = await branches.UserStateAsync();
        if (state?.IsActive != true)
        {
            http.Response.StatusCode = 403;
            return;
        }

        var profileCode = BusinessProfiles.ResolveLegacyCode(state.BusinessProfileCode);
        if (!BusinessProfiles.IsSupported(profileCode) || profileCode != BusinessProfiles.Electrical && !state.BusinessProfileConfigurationAvailable)
        {
            http.Response.StatusCode = 403;
            await http.Response.WriteAsJsonAsync(new
            {
                message = "This business profile is not enabled in the current release. Contact your administrator."
            });
            return;
        }
        scope.BusinessProfileCode = profileCode;
        scope.BusinessProfilePersisted = state.BusinessProfilePersisted;
        scope.BusinessProfileVersion = state.BusinessProfileVersion;
        scope.BusinessProfileConfigurationAvailable = state.BusinessProfileConfigurationAvailable;
        scope.ProfileCatalogSwitchingAvailable = state.ProfileCatalogSwitchingAvailable;
        scope.ProfileDataVisibilityAvailable = state.ProfileDataVisibilityAvailable;
        scope.CatalogRegistrationEnhancementsAvailable = state.CatalogRegistrationEnhancementsAvailable;
        scope.ProductImageGalleryAvailable = state.ProductImageGalleryAvailable;

        scope.IsOwner = state.IsOwner;
        scope.CanReviewStores = state.IsOwner || state.IsAdmin;
        long.TryParse(http.User.FindFirstValue("LocationId"), out var location);
        long.TryParse(http.User.FindFirstValue("StoreId"), out var store);
        var path = http.Request.Path.Value?.ToLowerInvariant() ?? "";
        var contextRoute = path.StartsWith("/api/branches/context") || path.StartsWith("/account/");
        var locations = (await branches.LocationsAsync()).ToList();
        var selected = locations.SingleOrDefault(l => l.LocationId == location);
        if (!scope.CanReviewStores && !path.StartsWith("/account/"))
        {
            var assigned = StoreContextRules.DefaultLocation(locations, false);
            if (assigned is null || location != assigned.LocationId || store != (assigned.StoreId ?? 0))
            {
                http.Response.StatusCode = 403;
                await http.Response.WriteAsJsonAsync(new
                {
                    message = "Your assigned store changed or has no active sales location. Sign in again or contact your administrator."
                });
                return;
            }
        }

        if (!contextRoute && ((location > 0 && selected is null) || (store > 0 && !locations.Any(l => l.StoreId == store)) || location < 0 || store < 0))
        {
            http.Response.StatusCode = 403;
            await http.Response.WriteAsJsonAsync(new
            {
                message = "Store access changed. Select an assigned location again."
            });
            return;
        }

        scope.LocationId = contextRoute && scope.CanReviewStores ? 0 : location;
        scope.StoreId = contextRoute && scope.CanReviewStores ? 0 : selected?.StoreId ?? store;
        scope.LocationName = selected is null ? (store > 0 ? "All locations in selected store" : "All authorized stores / warehouses") : selected.StoreName + " / " + selected.LocationName;
        scope.Consolidated = scope.LocationId == 0;
        if (scope.Consolidated && !HttpMethods.IsGet(http.Request.Method) && new[]
        {
            "/api/sales",
            "/api/purchases",
            "/api/receiving",
            "/api/inventory",
            "/api/transfers",
            "/api/service",
            "/api/warranty"
        }.Any(path.StartsWith))
        {
            http.Response.StatusCode = 400;
            await http.Response.WriteAsJsonAsync(new
            {
                message = "Select one authorized stock location before posting. Consolidated views are read-only."
            });
            return;
        }

        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && http.Request.Headers.TryGetValue("X-StockFlow-Location", out var stamp) && long.TryParse(stamp, out var submitted) && submitted != location)
        {
            http.Response.StatusCode = 409;
            await http.Response.WriteAsJsonAsync(new
            {
                message = "The active store/location changed in another tab. Reload before saving."
            });
            return;
        }

        await next(http);
    }
}
