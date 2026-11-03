using System.Net;
using Microsoft.Extensions.Configuration;
using StockFlow.Web.Notifications;

namespace StockFlow.MasterDataChecks;

static class SenderVerificationChecks
{
    public static async Task Run()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {{"Notifications:Email:Tenants:1:ApiKey","test-secret"}}).Build();
        using var handler=new SenderHandler();
        using var client=new HttpClient(handler);
        var provider=new BrevoNotificationEmailProvider(client,config);
        async Task Check(long tenant,string sender,bool verified,string message)
        {
            var result=await provider.VerifySenderDetailsAsync(tenant,sender,CancellationToken.None);
            if(result.Verified!=verified || !result.Message.Contains(message,StringComparison.OrdinalIgnoreCase) || result.Message.Contains("test-secret"))
                throw new Exception("Unexpected sender verification result: "+message);
            Console.WriteLine("PASS: sender verification "+message);
        }
        await Check(1,"",false,"Save a sender");
        await Check(2,"shop@example.test",false,"API key is missing");
        if(handler.Calls!=0) throw new Exception("Missing sender/key must not contact the provider.");
        foreach(var pair in new[]{(HttpStatusCode.Unauthorized,"HTTP 401"),(HttpStatusCode.Forbidden,"HTTP 403"),
            (HttpStatusCode.TooManyRequests,"request limit"),(HttpStatusCode.ServiceUnavailable,"HTTP 503")})
        {
            handler.Status=pair.Item1;
            await Check(1,"shop@example.test",false,pair.Item2);
        }
        handler.Status=HttpStatusCode.OK;
        handler.Body="{\"senders\":[{\"email\":\"SHOP@example.test\",\"active\":true}]}";
        await Check(1," shop@example.test ",true,"confirmed");
        handler.Body="{\"senders\":[{\"email\":\"shop@example.test\",\"active\":false}]}";
        await Check(1,"shop@example.test",false,"not active");
        handler.Body="{\"senders\":[{\"email\":\"other@example.test\",\"active\":true}]}";
        await Check(1,"shop@example.test",false,"not found");
        foreach(var body in new[]{"not-json","[]","{\"senders\":{}}"})
        {
            handler.Body=body;
            await Check(1,"shop@example.test",false,"unreadable");
        }
        handler.Error=new HttpRequestException("test-secret");
        await Check(1,"shop@example.test",false,"could not connect");
        handler.Error=new TaskCanceledException("test-secret");
        await Check(1,"shop@example.test",false,"timed out");
    }
    sealed class SenderHandler:HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status=HttpStatusCode.OK;
        public string Body="{}";
        public Exception? Error;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Calls++;
            if(request.Method!=HttpMethod.Get || request.RequestUri?.AbsoluteUri!="https://api.brevo.com/v3/senders" || !request.Headers.Contains("api-key"))
                throw new Exception("Sender verification must only query the fixed provider endpoint.");
            return Error is not null ? Task.FromException<HttpResponseMessage>(Error)
                : Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body)});
        }
    }
}
