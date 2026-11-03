using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
internal static class InvoiceUiChecks
{
    public static async Task Run(IPage page,string baseUrl)
    {
        var ids = await File.ReadAllLinesAsync("tests/bin/phase10-invoice-ids.txt");
        await page.GotoAsync(baseUrl+"/Sales/Invoice/"+ids[0]);
        await page.Locator(".invoice-paper").WaitForAsync();
        var content = await page.Locator(".invoice-paper").InnerTextAsync();
        if (!content.Contains("Original Test Store") || !content.Contains("BAT002") || !content.Contains("Original Model") || !content.Contains("UPI")) throw new Exception("Invoice content missing");
        foreach (var width in new[] {1440,768,390,320})
        {
            await page.SetViewportSizeAsync(width,900); await page.WaitForTimeoutAsync(300);
            if(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth")) throw new Exception("Invoice screen overflow "+width);
            await page.ScreenshotAsync(new() { Path=$"tests/bin/ui-screens/invoice-phase10-{width}.png",FullPage=true });
        }
        await page.EvaluateAsync("() => { window.printCount=0; window.print=()=>window.printCount++; }");
        await page.Locator("#print-invoice").ClickAsync(); await page.Locator("#save-invoice-pdf").ClickAsync();
        var printCount=await page.EvaluateAsync<int>("window.printCount");
        if(printCount!=2) throw new Exception("Print/PDF buttons invoked print "+printCount+" times. Scripts: "+await page.EvaluateAsync<string>("JSON.stringify([...document.scripts].map(s=>s.src))"));
        await page.SetViewportSizeAsync(794,1123);
        await page.EmulateMediaAsync(new() { Media=Media.Print });
        if(!await page.EvaluateAsync<bool>("['.sf-sidebar','.sf-topbar','.sf-app-footer','.invoice-actions'].every(s=>getComputedStyle(document.querySelector(s)).display==='none')")) throw new Exception("App controls leaked into print");
        if(!await page.EvaluateAsync<bool>("document.querySelector('.invoice-items').scrollWidth<=document.querySelector('.invoice-paper').clientWidth")) throw new Exception("Printed table clipped");
        await page.ScreenshotAsync(new() { Path="tests/bin/ui-screens/invoice-phase10-print.png",FullPage=true });
        var pdf = await page.PdfAsync(new() { Path="tests/bin/invoice-phase10.pdf",PreferCSSPageSize=true,PrintBackground=true });
        if(!Encoding.ASCII.GetString(pdf,0,5).StartsWith("%PDF-")) throw new Exception("Invalid PDF output");
        await page.GotoAsync(baseUrl+"/Sales/Invoice/"+ids[1]);
        var longPdf = await page.PdfAsync(new() { Path="tests/bin/invoice-phase10-multipage.pdf",PreferCSSPageSize=true,PrintBackground=true });
        var pages = Regex.Matches(Encoding.Latin1.GetString(longPdf),@"/Type\s*/Page\b").Count;
        if(pages<2 || await page.Locator(".invoice-items tbody tr").CountAsync()!=100) throw new Exception("Long invoice did not produce a multi-page PDF");
        await page.EmulateMediaAsync(new() { Media=Media.Screen });
        await page.GotoAsync(baseUrl+"/Sales");
        await page.Locator("#sale-search").FillAsync("0098765432"); await page.Locator("#sales-filter button").ClickAsync();
        await page.Locator("#sales-rows a[href='/Sales/Invoice/"+ids[0]+"']").WaitForAsync();
        await page.GotoAsync(baseUrl+"/Scan"); await page.Locator("#scanInput").FillAsync("BAT002"); await page.Locator("#scanInput").PressAsync("Enter");
        await page.Locator("#scan-invoice:not([hidden])").WaitForAsync();
        if(await page.Locator("#scan-invoice").GetAttributeAsync("href")!="/Sales/Invoice/"+ids[0]) throw new Exception("Sold serial invoice link incorrect");
        Console.WriteLine($"PASS: invoice display, 4 screen sizes, print controls/CSS, PDF generation ({pages} pages for 100 lines), historical mobile search and scan-to-invoice.");
    }
}
