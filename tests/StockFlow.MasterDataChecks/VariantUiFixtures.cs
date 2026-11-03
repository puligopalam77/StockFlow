namespace StockFlow.MasterDataChecks;
internal static class VariantUiFixtures
{
    public static async Task Write(Factory factory)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"StockFlow.sln")))root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests/bin/variant-ui-fixtures");Directory.CreateDirectory(output);
        using var client=factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role","Admin");
        client.DefaultRequestHeaders.Add("Test-UserId","1");
        foreach(var page in new[]{"Products/Variants","Purchases/Create","Purchases/Details/1","Sales/Create","Inventory","Transfers","Admin/Stores","Reports/Stores"})
            await File.WriteAllTextAsync(Path.Combine(output,page.Replace('/','-')+".html"),await client.GetStringAsync('/'+page));
        await File.WriteAllTextAsync(Path.Combine(output,"Purchases-Order.html"),await client.GetStringAsync("/Purchases/Create?mode=order"));
        Console.WriteLine("Rendered variant UI fixtures.");
    }
}
