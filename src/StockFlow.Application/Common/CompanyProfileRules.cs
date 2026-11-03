using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Invoices;
namespace StockFlow.Application.Common;
public static class CompanyProfileRules
{
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
