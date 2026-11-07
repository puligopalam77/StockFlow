using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Invoices;
namespace StockFlow.Application.Common;
public static class CompanyProfileRules
{
    public static void ValidateNumbering(CompanySettings settings,int? invoiceLength,int? receiptLength,int year)
    {
        foreach(var (prefix,limit,field) in new[]{(settings.InvoicePrefix,invoiceLength,nameof(settings.InvoicePrefix)),(settings.ReceiptPrefix,receiptLength,nameof(settings.ReceiptPrefix))})
            if(limit is {} size && prefix.Replace("{YYYY}",year.ToString(System.Globalization.CultureInfo.InvariantCulture)).Length+6>size)
                throw new ValidationException($"{field}: shorten the prefix to leave room for the year and at least six sequence digits within the document's {size}-character limit.",null,field);
    }
    public static void ValidateTaxDisplay(CompanySettings settings,CompanySettings previous)
    {
        if(settings.GstNumber==previous.GstNumber && settings.PanNumber==previous.PanNumber && settings.State==previous.State)return;
        if(string.IsNullOrWhiteSpace(settings.GstNumber))return;
        if(!GstRules.ValidGstin(settings.GstNumber))throw new ValidationException("Check the company GSTIN format and checksum. This does not verify GST Portal registration.",null,nameof(settings.GstNumber));
        var state=GstRules.States.FirstOrDefault(s=>s.Key!="28"&&string.Equals(s.Value,settings.State,StringComparison.OrdinalIgnoreCase)).Key;
        if(state is not null && !settings.GstNumber.StartsWith(state,StringComparison.Ordinal))throw new ValidationException("The company GSTIN must match the selected company state.",null,nameof(settings.GstNumber));
        if(!string.IsNullOrWhiteSpace(settings.PanNumber)&&settings.GstNumber.Substring(2,10)!=settings.PanNumber)throw new ValidationException("The company PAN must match the company GSTIN.",null,nameof(settings.PanNumber));
    }
    public static void NormalizeAddress(CompanySettings settings)
    {
        settings.AddressLine1=GstRules.Clean(settings.AddressLine1);
        settings.AddressLine2=GstRules.Clean(settings.AddressLine2);
        settings.District=GstRules.Clean(settings.District);
        if(settings.AddressLine1 is null && settings.AddressLine2 is null && settings.District is null)return;
        if(settings.AddressLine1 is null)throw new ValidationException("Enter Address Line 1 before saving structured address details.");
        // The existing invoice renderer prints City, State and PIN separately.
        settings.Address=PostalAddress.Format(settings.AddressLine1,settings.AddressLine2,district:settings.District);
    }
    public static void Validate(CompanySettings settings)
    {
        foreach(var prefix in new[]{settings.InvoicePrefix ?? "",settings.ReceiptPrefix ?? ""})
            if(prefix.Replace("{YYYY}","").Contains('{') || prefix.Replace("{YYYY}","").Contains('}'))
                throw new ValidationException("The only supported prefix placeholder is {YYYY}.");
        if(settings.LogoDataUrl is null) return;
        var png=settings.LogoDataUrl.StartsWith("data:image/png;base64,",StringComparison.Ordinal);
        var jpg=settings.LogoDataUrl.StartsWith("data:image/jpeg;base64,",StringComparison.Ordinal);
        if(!png && !jpg || settings.LogoDataUrl.Length>2800000) throw new ValidationException("Choose a PNG or JPG logo up to 2 MB.");
        byte[] bytes;
        try {bytes=Convert.FromBase64String(settings.LogoDataUrl[(settings.LogoDataUrl.IndexOf(',')+1)..]);}
        catch(FormatException){throw new ValidationException("The logo image is invalid. Choose the file again.");}
        if(bytes.Length is < 8 or > 2000000 || (png ? !bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) : bytes[0]!=255 || bytes[1]!=216 || bytes[2]!=255))
            throw new ValidationException("Choose a valid PNG or JPG logo up to 2 MB.");
    }
}
