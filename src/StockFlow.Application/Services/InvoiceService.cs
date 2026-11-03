using StockFlow.Application.DTOs.Invoices;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository repository;
    private readonly ICompanySettingsRepository companySettings;
    // ASP.NET supplies these services when it creates this class.
    public InvoiceService(IInvoiceRepository repository, ICompanySettingsRepository companySettings)
    {
        this.repository = repository;
        this.companySettings = companySettings;
    }

    public async Task<InvoiceResponse?> GetInvoiceAsync(long saleId)
    {
        if (saleId > 0)
        {
            var invoice = await repository.GetInvoiceAsync(saleId);
            if (invoice is not null)
            {
                // Branding follows the shop's saved logo, including on older invoices.
                // All other historical company details and financial values stay intact.
                invoice.Company.LogoDataUrl = (await companySettings.GetAsync()).LogoDataUrl;
            }
            return invoice;
        }
        else
        {
            return null;
        }
    }
}
