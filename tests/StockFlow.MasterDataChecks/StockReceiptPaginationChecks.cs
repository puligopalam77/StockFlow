using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockFlow.Application.DTOs.Inventory;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

internal static class StockReceiptPaginationChecks
{
    public static async Task Run(Factory factory)
    {
        var repository=new ReceiptRepository();
        using var app=factory.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{
            services.RemoveAll<IStockReceiptHistoryRepository>();
            services.AddSingleton<IStockReceiptHistoryRepository>(repository);
        }));
        using var client=app.CreateClient();client.DefaultRequestHeaders.Add("Test-Role","Admin");
        foreach(var endpoint in new[]{"/api/stock-receipts","/api/stock-receipts/items"}){
            foreach(var size in new[]{10,25,50,100}){
                var result=await client.GetAsync(endpoint+"?page=2&pageSize="+size);
                Check(result.IsSuccessStatusCode,"accepted "+endpoint+" size "+size);
                Check(repository.Last?.Page==2&&repository.Last.PageSize==size&&repository.Last.Offset==size,"exact page and size reach the repository");
            }
            Check((await client.GetAsync(endpoint)).IsSuccessStatusCode&&repository.Last?.PageSize==10,"default matches shared ten-row control");
            foreach(var query in new[]{"?page=0&pageSize=10","?page=-1&pageSize=10","?page=1&pageSize=0","?page=1&pageSize=11","?page=1&pageSize=101","?page=1&pageSize=-10","?page=1.5&pageSize=10"}){
                var calls=repository.Calls;
                Check((int)(await client.GetAsync(endpoint+query)).StatusCode==400,"invalid pagination rejected: "+query);
                Check(repository.Calls==calls,"invalid request does not query data");
            }
            var before=repository.Calls;
            client.DefaultRequestHeaders.Add("Test-Deny","STOCK_INFLOW");
            Check((int)(await client.GetAsync(endpoint+"?pageSize=10")).StatusCode==403&&repository.Calls==before,"ten-row paging retains permissions");
            client.DefaultRequestHeaders.Remove("Test-Deny");
        }
        Console.WriteLine("PASS: Receive Stock receipts and item movements accept shared page sizes/default, reject invalid inputs and retain authorization; no database used.");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private sealed class ReceiptRepository : IStockReceiptHistoryRepository
    {
        public StockReceiptSearch? Last; public int Calls;
        public Task<StockReceiptHistoryResult> SearchAsync(StockReceiptSearch search){Last=search;Calls++;return Task.FromResult(new StockReceiptHistoryResult{Page=search.Page,PageSize=search.PageSize});}
        public Task<StockReceiptItemHistoryResult> SearchItemsAsync(StockReceiptSearch search){Last=search;Calls++;return Task.FromResult(new StockReceiptItemHistoryResult{Page=search.Page,PageSize=search.PageSize});}
        public Task<IReadOnlyList<StockReceiptSupplier>> GetSuppliersAsync()=>Task.FromResult<IReadOnlyList<StockReceiptSupplier>>([]);
        public Task<StockReceiptDetail?> GetDetailAsync(string source,long id)=>Task.FromResult<StockReceiptDetail?>(null);
    }
}
