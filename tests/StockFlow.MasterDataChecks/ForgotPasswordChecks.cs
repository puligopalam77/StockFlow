using System.Net;
using System.Text.RegularExpressions;

namespace StockFlow.MasterDataChecks;

internal static class ForgotPasswordChecks
{
    public static async Task Run(Factory factory)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/Account/ForgotPassword");
        if (html.Contains("Input_CurrentPassword")) throw new Exception("Current password must be removed.");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (token.Length == 0) throw new Exception("Password page must have CSRF protection.");
        var login = await client.GetStringAsync("/Account/Login");
        var prefilled = await client.GetStringAsync("/Account/ForgotPassword?username=admin");
        if (!prefilled.Contains("value=\"admin\"")) throw new Exception("Login username must prefill the password page.");
        var special = await client.GetStringAsync("/Account/ForgotPassword?username=admin%20%26%20sales");
        if (!special.Contains("value=\"admin &amp; sales\"")) throw new Exception("Username must be safely encoded.");
        var oversized = await client.GetStringAsync("/Account/ForgotPassword?username=" + new string('a',51));
        if (oversized.Contains("value=\"" + new string('a',51))) throw new Exception("Oversized query usernames must not be loaded.");
        if (!login.Contains("href=\"/Account/ForgotPassword\"")) throw new Exception("Forgot password must navigate to its page.");
        async Task<HttpResponseMessage> Submit(string username = "admin", string password = "New secure phrase 123!", string? confirm = null, bool csrf = true)
        {
            var values = new Dictionary<string,string> {
                ["Input.Username"] = username, ["Input.Password"] = password,
                ["Input.ConfirmPassword"] = confirm ?? password,
                // An extra posted credential must not enable the removed flow.
                ["Input.CurrentPassword"] = "current-secret"
            };
            if (csrf) values["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
            return await client.PostAsync("/Account/ForgotPassword", new FormUrlEncodedContent(values));
        }
        if ((await Submit(csrf:false)).StatusCode != HttpStatusCode.BadRequest)
            throw new Exception("CSRF protection must remain enabled.");
        foreach (var response in new[] { await Submit(username:""), await Submit(password:"short"),
            await Submit(password:new string('a',129)), await Submit(password:new string('a',12)),
            await Submit(confirm:"Mismatch phrase 123!") })
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Invalid fields must show the password page.");
        var responseWithoutProof = await Submit();
        var responseHtml = await responseWithoutProof.Content.ReadAsStringAsync();
        if (responseWithoutProof.StatusCode != HttpStatusCode.OK || !responseHtml.Contains("Account verification is required"))
            throw new Exception("A username alone must not authorize a password reset.");
        if (responseHtml.Contains("value=\"New secure phrase 123!\""))
            throw new Exception("Failed forms must not redisplay passwords.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root = root.Parent;
        var output = Path.Combine(root!.FullName,"tests/bin/password-page-fixtures");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"forgot.html"),html);
        await File.WriteAllTextAsync(Path.Combine(output,"forgot-prefilled.html"),prefilled);
        await File.WriteAllTextAsync(Path.Combine(output,"login.html"),login);
        Console.WriteLine("PASS: removed current-password field, navigation, CSRF, mandatory/length/matching fields and blocking unverified resets.");
    }
}
