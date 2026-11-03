namespace StockFlow.MasterDataChecks;
internal static class ServiceUiFixtures
{
    public static async Task Write(Factory factory)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null && !File.Exists(Path.Combine(root.FullName,"StockFlow.sln"))) root=root.Parent;
        var output=Path.Combine(root!.FullName,"tests","bin","phase12-ui-fixtures");
        Directory.CreateDirectory(output);
        using var client=factory.CreateClient();
        client.DefaultRequestHeaders.Add("Test-Role","Service");
        client.DefaultRequestHeaders.Add("Test-UserId","1");
        foreach(var page in new[]{"Create","Details/1","Index","Pending"})
        {
            var html=await client.GetStringAsync("/Service/"+page);
            await File.WriteAllTextAsync(Path.Combine(output,page.Replace('/','-')+".html"),html);
        }
        Console.WriteLine("Rendered service browser-test fixtures: "+output);
    }
}
