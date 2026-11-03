using StockFlow.Mobile;
foreach (var value in new[] { "http://shop.example", "https://localhost", "https://127.0.0.1", "https://user:password@shop.example", "https://shop.example/path", "https://shop.example/?token=x", "https://shop.example/#fragment", "javascript:alert(1)", "file:///data/data/app", "" })
    Check(!ServerAddress.TryParse(value, out _), "reject invalid server configuration");
Check(ServerAddress.TryParse(" https://SHOP.example:8443/ ", out var address), "accept hosted HTTPS origin");
Check(address!.Contains("https://shop.example:8443/Sales?fromReports=1"), "allow existing screen URLs");
Check(address.Contains("https://shop.example:8443/Account/Login?returnUrl=%2FSales"), "allow server login redirect");
foreach (var value in new[] { "https://shop.example/Sales", "https://shop.example.evil.test:8443/Sales", "http://shop.example:8443/Sales", "https://user@shop.example:8443/Sales", "https://other.example:8443/Sales", "data:text/html,hello" })
    Check(!address.Contains(value), "reject cross-origin/insecure navigation");
Check(address.SignInUrl == "https://shop.example:8443/Account/Login", "use existing authentication screen");
Console.WriteLine("PASS: HTTPS configuration, same-origin navigation, port separation and existing login route.");
Check(DevicePolicy.FileName("../bad/file\n.pdf") == "_bad_file_.pdf", "sanitize output file names");
Check(DevicePolicy.Mime("application/pdf; charset=utf-8") == "application/pdf", "allow PDF");
Check(DevicePolicy.Mime("text/html") == "application/octet-stream", "do not save active HTML as a document");
Console.WriteLine("PASS: document names and safe MIME types.");
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
