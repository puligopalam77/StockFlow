using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

internal static class SharedSessionDatabaseChecks
{
    public static async Task Run(NpgsqlConnection admin,string connection)
    {
        static void Check(bool passed,string description){if(!passed)throw new Exception(description);Console.WriteLine("PASS: "+description);}
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",connection}}).Build();
        var factory=new DbConnectionFactory(configuration,new BusinessScope());
        var options=Options.Create(new SessionSecurityOptions());
        var first=new PostgresSessionStore(factory,options);var second=new PostgresSessionStore(new(configuration,new BusinessScope()),options);
        var owner=new SessionOwner(1,1,await admin.ExecuteScalarAsync<long>("SELECT security_version FROM users WHERE user_id=1"));
        string Id()=>Guid.NewGuid().ToString("N").ToUpperInvariant();
        var id=Id();await first.CreateAsync(owner,id,"127.0.0.1","Database test browser");
        Check((await second.ValidateAsync(owner,id)).Active,"another application instance reads the same session");
        await Task.WhenAll(first.ValidateAsync(owner,id,0),second.ValidateAsync(owner,id,0));
        Check((await second.ListAsync(owner)).Single().SessionId==id,"shared activity renews one stored session");
        Check(!(await second.ValidateAsync(owner with{TenantId=2},id)).Active,"cross-tenant session lookup is rejected by RLS");
        Check(!(await second.ValidateAsync(owner with{UserId=2},id)).Active,"same-tenant foreign-user lookup is rejected by RLS");
        Check(!await second.RevokeAsync(owner with{UserId=2},id,"USER_REVOKED"),"foreign-user revocation is rejected");
        Check(await first.RevokeAsync(owner,id,"LOGOUT"),"manual logout ends the shared session");
        Check(!(await second.ValidateAsync(owner,id,0)).Active,"activity on another server cannot revive a revoked session");
        Check(!await second.RevokeAsync(owner,id,"LOGOUT"),"repeat logout is idempotent");
        Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE action_name='SESSION_ENDED' AND reason='LOGOUT'")==1,"manual logout audit is written exactly once");
        var idle=Id();await first.CreateAsync(owner,idle,"127.0.0.1","Idle browser");
        await admin.ExecuteAsync("UPDATE auth_sessions SET idle_expires_at=now()-interval '1 second' WHERE session_id=@id",new{id=Guid.Parse(idle)});
        await Task.WhenAll(first.SweepAsync(),second.SweepAsync());
        Check(!(await second.ValidateAsync(owner,idle)).Active,"shared worker rejects idle sessions even when their browser has closed");
        Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE reason='IDLE_TIMEOUT'")==1,"competing sweep workers audit expiry once");
        var absolute=Id();await first.CreateAsync(owner,absolute,"127.0.0.1","Active browser");
        await admin.ExecuteAsync("UPDATE auth_sessions SET created_at=now()-interval '9 hours',absolute_expires_at=now()-interval '1 second',idle_expires_at=now()-interval '1 second' WHERE session_id=@id",new{id=Guid.Parse(absolute)});
        Check((await second.ValidateAsync(owner,absolute,0)).Reason=="ABSOLUTE_TIMEOUT","activity never extends the absolute deadline");
        var changed=Id();await first.CreateAsync(owner,changed,"127.0.0.1","Changed account");
        await admin.ExecuteAsync("UPDATE users SET security_version=security_version+1 WHERE user_id=1");
        Check((await second.ValidateAsync(owner,changed)).Reason=="SECURITY_CHANGED","password/security version changes invalidate shared sessions");
        var bucket=new string('A',64);
        Check(await first.TakeLoginAttemptAsync(bucket,2,60)==0 && await second.TakeLoginAttemptAsync(bucket,2,60)==0
            && await first.TakeLoginAttemptAsync(bucket,2,60)>0,"login throttling is shared across application instances");
        await admin.ExecuteAsync("UPDATE auth_login_limits SET window_started_at=now()-interval '2 minutes'");
        Check(await second.TakeLoginAttemptAsync(bucket,2,60)==0,"login rate limits reset after their window");
        ServiceProvider KeyProvider(System.Security.Cryptography.X509Certificates.X509Certificate2? certificate=null){var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<SharedProtectionKeys>();services.AddSingleton<IConfigureOptions<KeyManagementOptions>,ConfigureSharedProtectionKeys>();
            var protection=services.AddDataProtection().SetApplicationName("StockFlow");if(certificate is not null)protection.ProtectKeysWithCertificate(certificate);return services.BuildServiceProvider();}
        using var keys1=KeyProvider();using var keys2=KeyProvider();
        var payload=keys1.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test").Protect("safe cross-server cookie");
        Check(keys2.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test").Unprotect(payload)=="safe cross-server cookie","shared database keys decrypt cookies across separate service providers");
        using var rsa=System.Security.Cryptography.RSA.Create(2048);
        var request=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=StockFlow session tests",rsa,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(2));
        using(var encryptedKeys=KeyProvider(certificate))encryptedKeys.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(1));
        Check((await admin.ExecuteScalarAsync<string>("SELECT xml FROM auth_protection_keys ORDER BY key_id DESC LIMIT 1"))?.Contains("encryptedSecret")==true,"configured certificate encrypts newly persisted shared keys at rest");
        using var certKeys1=KeyProvider(certificate);using var certKeys2=KeyProvider(certificate);
        var encryptedCookie=certKeys1.GetRequiredService<IDataProtectionProvider>().CreateProtector("certificate-cookie-test").Protect("certificate protected payload");
        Check(certKeys2.GetRequiredService<IDataProtectionProvider>().CreateProtector("certificate-cookie-test").Unprotect(encryptedCookie)=="certificate protected payload","matching certificates decrypt shared keys on another instance");
        await using var runtime=factory.CreateSecurityConnection(1,1);
        try{await runtime.QueryAsync("SELECT * FROM auth_protection_keys");throw new Exception("runtime can access raw key table");}catch(PostgresException error)when(error.SqlState=="42501"){Console.WriteLine("PASS: raw shared key table is not granted to runtime");}
        Check(await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM audit_logs WHERE new_values::text LIKE @pattern",new{pattern="%"+id+"%"})==0,"audit logs exclude raw session identifiers and cookies");
        Console.WriteLine("Shared-session database checks ran in an isolated database using migration 033 and the restricted runtime role.");
    }
}
