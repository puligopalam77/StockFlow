using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using StockFlow.Application.Common;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Web.Security;

public sealed class SharedProtectionKeys : IXmlRepository
{
    private readonly IConfiguration configuration;
    // ASP.NET supplies these services when it creates this class.
    public SharedProtectionKeys(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    private DbConnectionFactory Factory => new(configuration, new BusinessScope());

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var c = Factory.CreateSecurityConnection(0, 0);
        return c.Query<string>("SELECT sf_read_protection_keys()").Select(XElement.Parse).ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var c = Factory.CreateSecurityConnection(0, 0);
        c.Execute("SELECT sf_write_protection_key(@friendlyName,@xml)",
new
{
    friendlyName,
    xml = element.ToString(SaveOptions.DisableFormatting)
});
    }
}

public sealed class ConfigureSharedProtectionKeys : IConfigureOptions<KeyManagementOptions>
{
    private readonly SharedProtectionKeys repository;
    // ASP.NET supplies these services when it creates this class.
    public ConfigureSharedProtectionKeys(SharedProtectionKeys repository)
    {
        this.repository = repository;
    }

    public void Configure(KeyManagementOptions options)
    {
        options.XmlRepository = repository;
    }
}
