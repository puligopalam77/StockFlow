using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;
internal static class LoginValidationChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> Submit(string username, string password)
        {
            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Input.TenantCode"]="DEFAULT", ["Input.Username"]=username,
                ["Input.Password"]=password, ["__RequestVerificationToken"]=token
            }));
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Invalid sign-in must return the form.");
            return await response.Content.ReadAsStringAsync();
        }
        var empty = await Submit("", "");
        if (!empty.Contains("Username is required.") || !empty.Contains("Password is required."))
            throw new Exception("Required server messages must name both fields.");
        var invalid = await Submit("admin", "invalid-login-secret");
        if (!invalid.Contains("Invalid username or password.")) throw new Exception("Invalid credentials need a clear message.");
        if (invalid.IndexOf("sf-login-errors") > invalid.IndexOf("class=\"sf-signin\""))
            throw new Exception("Credential errors must appear above Sign In.");
        if (invalid.Contains("value=\"invalid-login-secret\"")) throw new Exception("Passwords must not be redisplayed.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var output = Path.Combine(root!.FullName,"tests/bin/password-page-fixtures");
        Directory.CreateDirectory(output);
        using var inactiveApp = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IAuthService>();
            services.AddScoped<IAuthService, InactiveLoginAuthService>();
        }));
        using var inactiveClient = inactiveApp.CreateClient(new() { AllowAutoRedirect = false });
        var inactiveForm = await inactiveClient.GetStringAsync("/Account/Login");
        var inactiveToken = WebUtility.HtmlDecode(Regex.Match(inactiveForm,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        async Task<string> SubmitInactive(string password)
        {
            var response = await inactiveClient.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string> {
                ["Input.TenantCode"]="DEFAULT", ["Input.Username"]="inactive-user", ["Input.Password"]=password,
                ["__RequestVerificationToken"]=inactiveToken
            }));
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Inactive users must remain on the sign-in page.");
            return await response.Content.ReadAsStringAsync();
        }
        var inactiveHtml = await SubmitInactive("verified-test-secret");
        if (!inactiveHtml.Contains(LoginResult.InactiveMessage) || inactiveHtml.Contains("value=\"verified-test-secret\""))
            throw new Exception("Inactive message must be displayed without exposing the password.");
        var wrongInactive = await SubmitInactive("wrong");
        if (wrongInactive.Contains(LoginResult.InactiveMessage) || !wrongInactive.Contains("Invalid username or password."))
            throw new Exception("Wrong credentials must not reveal an inactive account.");
        await File.WriteAllTextAsync(Path.Combine(output,"login-empty.html"),empty);
        await File.WriteAllTextAsync(Path.Combine(output,"login-invalid.html"),invalid);
        await File.WriteAllTextAsync(Path.Combine(output,"login-inactive.html"),inactiveHtml);
        Console.WriteLine("PASS: login server required-field messages, invalid-credentials placement and no password redisplay.");
        using var passwordApp=factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
        {
            services.RemoveAll<IAuthService>();
            services.AddScoped<IAuthService,PasswordOnlyAuthService>();
            services.RemoveAll<StockFlow.Web.Notifications.INotificationEmailProvider>();
            services.AddSingleton<StockFlow.Web.Notifications.INotificationEmailProvider,UnavailableEmailProvider>();
        }));
        using var passwordClient=passwordApp.CreateClient(new(){AllowAutoRedirect=false});
        var passwordForm=await passwordClient.GetStringAsync("/Account/Login");
        var passwordToken=WebUtility.HtmlDecode(Regex.Match(passwordForm,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var signedIn=await passwordClient.PostAsync("/Account/Login",new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Input.TenantCode"]="DEFAULT",["Input.Username"]="password-user",["Input.Password"]="verified-password",
            ["Input.UseEmailOtp"]="true",["__RequestVerificationToken"]=passwordToken
        }));
        if(signedIn.StatusCode!=HttpStatusCode.Redirect || signedIn.Headers.Location?.OriginalString!="/Account/Home"
            || !signedIn.Headers.TryGetValues("Set-Cookie",out var cookies) || !cookies.Any(x=>x.StartsWith("StockFlow.Auth=")))
            throw new Exception("Password login must issue a session without depending on email delivery.");
        if(passwordApp.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Any(x=>string.Equals(x.RoutePattern.RawText?.Trim('/'),"Account/EmailOtp",StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Retired email OTP endpoint must no longer be available.");
        Console.WriteLine("PASS: password login creates a session with an unavailable email provider; email OTP route is removed.");
    }
}

internal sealed class PasswordOnlyAuthService:IAuthService
{
    public Task<LoginResult> ValidateLoginAsync(LoginRequest request)=>Task.FromResult(new LoginResult
    {
        Success=true,UserId=1,TenantId=1,StoreId=1,LocationId=1,SecurityVersion=1,
        Username="password-user",FullName="Password User",RoleName="Admin"
    });
}
internal sealed class UnavailableEmailProvider:StockFlow.Web.Notifications.INotificationEmailProvider
{
    public Task<bool> VerifySenderAsync(long tenant,string sender,CancellationToken token)=>throw new Exception("Login must not verify an email sender.");
    public Task<StockFlow.Web.Notifications.EmailResult> SendAsync(StockFlow.Web.Notifications.EmailJob job,CancellationToken token)=>throw new Exception("Login must not send email.");
}

internal class InactiveLoginAuthService : IAuthService
{
    public Task<LoginResult> ValidateLoginAsync(LoginRequest request) => Task.FromResult(
        request.Password == "verified-test-secret"
            ? new LoginResult { ErrorCode = LoginResult.AccountInactive, ErrorMessage = LoginResult.InactiveMessage }
            : new LoginResult());
}
