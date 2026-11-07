using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

internal static class IdleSessionChecks
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class LoginService : IAuthService
    {
        public Task<LoginResult> ValidateLoginAsync(LoginRequest request) => Task.FromResult(new LoginResult
        { Success = true, UserId = 1, Username = "Admin", FullName = "Example Admin", RoleName = "Admin", TenantId = 1, StoreId = 1, LocationId = 1, SecurityVersion = 1 });
    }
    public static async Task Run(Factory source)
    {
        static void Check(bool condition, string description)
        { if (!condition) throw new Exception(description); Console.WriteLine("PASS: " + description); }
        var clock = new Clock();
        using var factory = source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IAuthService>(); services.AddSingleton<IAuthService, LoginService>();
            // Exercise the optional idle policy independently of the disabled app default.
            services.PostConfigure<SessionSecurityOptions>(options => options.IdleMinutes = 15);
            services.PostConfigure<AuthenticationOptions>(options =>
            { options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultForbidScheme = CookieAuthenticationDefaults.AuthenticationScheme; });
            services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options => options.TimeProvider = clock);
        }));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Test-Role", "Admin");
        string sessionId = "";
        async Task<string> Login(bool remember)
        {
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var html = await client.GetStringAsync("/Account/Login");
            var token = WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string>
            { ["Input.Username"] = "Admin", ["Input.Password"] = "Example password 123", ["Input.TenantCode"] = "DEFAULT", ["Input.RememberMe"] = remember.ToString(), ["__RequestVerificationToken"] = token }));
            Check(response.StatusCode == HttpStatusCode.Redirect, "real cookie login succeeds");
            var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("StockFlow.Auth="));
            Check(cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)==remember,
                remember?"Remember Me creates a persistent browser cookie":"unchecked Remember Me creates a browser-session cookie");
            var csrf = await client.GetFromJsonAsync<JsonObject>("/api/antiforgery");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!["requestToken"]!.GetValue<string>());
            sessionId = Regex.Match(await client.GetStringAsync("/Products"), "data-session=\"([^\"]+)\"").Groups[1].Value;
            return cookie.Split(';')[0];
        }
        await Login(true);
        var page = await client.GetStringAsync("/Products");
        Check(page.Contains("id=\"idle-session\"") && page.Contains("/js/idle-session."), "authenticated pages contain shared inactivity tracker");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName,"tests/bin/idle-session-ui-fixtures");
        Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output,"Products.html"),page);
        clock.Advance(TimeSpan.FromMinutes(8));
        Check((await client.GetAsync("/api/products")).IsSuccessStatusCode, "background request before expiry is accepted");
        clock.Advance(TimeSpan.FromMinutes(7));
        Check((await client.GetAsync("/api/products")).StatusCode == HttpStatusCode.Unauthorized, "idle server session expires at 15 minutes despite background requests and Remember Me");
        Check((await client.PostAsJsonAsync("/account/session/activity", new { sessionId, ageMilliseconds = 0 })).StatusCode == HttpStatusCode.Unauthorized, "late activity cannot revive an expired session");
        await Login(false);
        for (var index = 0; index < 7; index++)
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            Check((await client.PostAsJsonAsync("/account/session/activity",new {sessionId,ageMilliseconds=0})).IsSuccessStatusCode, "working activity keeps login alive: " + (index+1)*5 + " minutes");
        }
        clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Check((await client.GetAsync("/api/products")).IsSuccessStatusCode, "session remains valid one second before idle deadline");
        clock.Advance(TimeSpan.FromSeconds(1));
        Check((await client.GetAsync("/api/products")).StatusCode == HttpStatusCode.Unauthorized, "session expires on the next second after inactivity");
        var replayCookie = await Login(false);
        Check((await client.PostAsJsonAsync("/account/session/activity",new{sessionId,ageMilliseconds=-1})).StatusCode == HttpStatusCode.BadRequest,"invalid activity timestamps are rejected");
        client.DefaultRequestHeaders.Add("Test-Force-Password","true");
        Check((await client.PostAsJsonAsync("/account/session/activity",new{sessionId,ageMilliseconds=0})).IsSuccessStatusCode,"password-change screen can keep its login active");
        client.DefaultRequestHeaders.Remove("Test-Force-Password");
        using(var noCsrf = factory.CreateClient(new() {AllowAutoRedirect=false,HandleCookies=false}))
        {
            noCsrf.DefaultRequestHeaders.Add("Cookie",replayCookie);noCsrf.DefaultRequestHeaders.Add("Test-Role","Admin");
            Check((await noCsrf.PostAsJsonAsync("/account/session/activity",new {sessionId,ageMilliseconds=0})).StatusCode == HttpStatusCode.BadRequest,"cross-site activity without antiforgery token is rejected");
        }
        Check((await client.PostAsJsonAsync("/account/session/expire",new{sessionId="previous-login"})).StatusCode == HttpStatusCode.Conflict,"an old tab cannot sign out a newer login");
        Check((await client.PostAsJsonAsync("/account/session/expire",new{sessionId})).StatusCode == HttpStatusCode.NoContent,"browser timeout revokes the login session");
        using(var replay = factory.CreateClient(new() {AllowAutoRedirect=false,HandleCookies=false}))
        {
            replay.DefaultRequestHeaders.Add("Cookie",replayCookie);replay.DefaultRequestHeaders.Add("Test-Role","Admin");
            Check((await replay.GetAsync("/api/products")).StatusCode == HttpStatusCode.Unauthorized,"logged-out cookie cannot be replayed in another tab");
        }
        Check((await client.GetStringAsync("/Account/Login?reason=idle")).Contains("15 minutes of inactivity"),"login explains idle sign-out");
        var sessions = factory.Services.GetRequiredService<ISharedSessionStore>();
        var owner=new SessionOwner(1,1,1);var first=Guid.NewGuid().ToString("N");var second=Guid.NewGuid().ToString("N");
        await sessions.CreateAsync(owner,first,"127.0.0.1","Test device");await sessions.CreateAsync(owner,second,"127.0.0.1","Other device");
        clock.Advance(TimeSpan.FromMinutes(10));await sessions.ValidateAsync(owner,first,0);
        clock.Advance(TimeSpan.FromMinutes(5));
        Check((await sessions.ValidateAsync(owner,first)).Active && !(await sessions.ValidateAsync(owner,second)).Active,"independent devices have independent idle deadlines");
        await Login(false);
        var otherDevice=Guid.NewGuid().ToString("N").ToUpperInvariant();var foreign=Guid.NewGuid().ToString("N").ToUpperInvariant();
        await sessions.CreateAsync(owner,otherDevice,"127.0.0.2","Other browser");
        await sessions.CreateAsync(new(1,2,1),foreign,"127.0.0.3","Another user");
        var devices=await client.GetFromJsonAsync<JsonArray>("/account/session/devices") ?? throw new Exception("Missing session list.");
        Check(devices.Any(item=>item!["sessionId"]!.GetValue<string>()==otherDevice) && !devices.Any(item=>item!["sessionId"]!.GetValue<string>()==foreign)
            && devices.Count(item=>item!["isCurrent"]!.GetValue<bool>())==1,"device listing exposes only the user's sessions and identifies this device");
        Check((await client.PostAsJsonAsync("/account/session/revoke",new{sessionId=foreign})).StatusCode==HttpStatusCode.NotFound,"users cannot revoke another user's session");
        Check((await client.PostAsJsonAsync("/account/session/revoke",new{sessionId=otherDevice})).StatusCode==HttpStatusCode.NoContent,"user can revoke their other device");
        Check(!(await sessions.ValidateAsync(owner,otherDevice)).Active,"revocation is authoritative in the shared store");
        var sessionsPage=await client.GetStringAsync("/Account/Sessions");
        await File.WriteAllTextAsync(Path.Combine(output,"Sessions.html"),sessionsPage);
        Check(sessionsPage.Contains("sessions-rows"),"personal sessions screen renders");
        Check((await client.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string>{{"SessionId","older-login"}}))).StatusCode==HttpStatusCode.Conflict,"normal logout from an old tab cannot sign out a newer login");
        Check((await client.PostAsync("/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string>{{"SessionId",sessionId}}))).StatusCode==HttpStatusCode.Redirect,"normal logout accepts the current session identifier");
        var fake=(FakeSharedSessionStore)sessions;
        Check(fake.Events.Any(item=>item.Reason=="LOGOUT") && fake.Events.Any(item=>item.Reason=="USER_REVOKED") && fake.Events.Any(item=>item.Reason=="IDLE_TIMEOUT"),"session start/logout/revocation/idle expiry are audited with separate reasons");
        Check(fake.Events.All(item=>item.Reference!=sessionId),"audit correlation values do not contain session tokens");
        await Login(false);
        fake.Unavailable=true;
        Check((await client.GetAsync("/api/products")).StatusCode==HttpStatusCode.ServiceUnavailable,"shared session outage fails closed with HTTP 503");
        fake.Unavailable=false;
        Check((await client.GetAsync("/api/products")).IsSuccessStatusCode,"temporary outage does not destroy an otherwise valid login");
        for(var index=0;index<95;index++)
        {
            clock.Advance(TimeSpan.FromMinutes(5));
            CheckActivity(await client.PostAsJsonAsync("/account/session/activity",new{sessionId,ageMilliseconds=0}));
        }
        clock.Advance(TimeSpan.FromMinutes(4)+TimeSpan.FromSeconds(59));
        CheckActivity(await client.PostAsJsonAsync("/account/session/activity",new{sessionId,ageMilliseconds=0}));
        clock.Advance(TimeSpan.FromSeconds(1));
        Check((await client.GetAsync("/api/products")).StatusCode==HttpStatusCode.Unauthorized,"eight-hour lifetime expires even while continuously active");
        await sessions.SweepAsync();Check(fake.Events.Any(item=>item.Reason=="ABSOLUTE_TIMEOUT"),"absolute expiry is audited even if its cookie has already expired");
        factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionSecurityOptions>>().Value.IdleMinutes = 0;
        await Login(false);
        var noIdlePage = await client.GetStringAsync("/Products");
        Check(noIdlePage.Contains("data-timeout=\"28800000\""), "disabled inactivity tracker uses the absolute eight-hour deadline");
        clock.Advance(TimeSpan.FromMinutes(16));
        Check((await client.GetAsync("/api/products")).IsSuccessStatusCode, "disabled inactivity expiry keeps the real cookie valid after sixteen minutes without activity");
        clock.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(43));
        Check((await client.GetAsync("/api/products")).IsSuccessStatusCode, "disabled inactivity expiry remains valid just before eight hours");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check((await client.GetAsync("/api/products")).StatusCode == HttpStatusCode.Unauthorized, "absolute lifetime still expires when inactivity logout is disabled");
        using(var throttleFactory=source.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            services.PostConfigure<SessionSecurityOptions>(value=>{value.LoginAttemptsPerIp=2;value.GlobalLoginAttemptsPerMinute=1000;}))))
        using(var throttle=throttleFactory.CreateClient(new(){AllowAutoRedirect=false}))
        {
            for(var index=0;index<2;index++)await throttle.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>()));
            throttle.DefaultRequestHeaders.Add("X-Forwarded-For","203.0.113.90");
            var blocked=await throttle.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>()));
            Check((int)blocked.StatusCode==429 && blocked.Headers.RetryAfter is not null,"login requests are throttled before authentication with Retry-After; untrusted proxy headers cannot bypass limits");
        }
        Console.WriteLine("Idle login checks use real authentication cookies and fake time/repositories; no application database accessed.");
        static void CheckActivity(HttpResponseMessage response){if(!response.IsSuccessStatusCode)throw new Exception("Working login ended before its maximum lifetime.");response.Dispose();}
    }
}
