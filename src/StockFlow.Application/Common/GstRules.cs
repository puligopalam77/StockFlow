using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.Common;

public static class GstRules
{
    public static readonly IReadOnlyDictionary<string, string> States = new Dictionary<string, string>
    {
        ["01"]="Jammu and Kashmir",["02"]="Himachal Pradesh",["03"]="Punjab",["04"]="Chandigarh",
        ["05"]="Uttarakhand",["06"]="Haryana",["07"]="Delhi",["08"]="Rajasthan",["09"]="Uttar Pradesh",
        ["10"]="Bihar",["11"]="Sikkim",["12"]="Arunachal Pradesh",["13"]="Nagaland",["14"]="Manipur",
        ["15"]="Mizoram",["16"]="Tripura",["17"]="Meghalaya",["18"]="Assam",["19"]="West Bengal",
        ["20"]="Jharkhand",["21"]="Odisha",["22"]="Chhattisgarh",["23"]="Madhya Pradesh",["24"]="Gujarat",
        ["26"]="Dadra and Nagar Haveli and Daman and Diu",["27"]="Maharashtra",["28"]="Andhra Pradesh (legacy)",
        ["29"]="Karnataka",["30"]="Goa",["31"]="Lakshadweep",["32"]="Kerala",["33"]="Tamil Nadu",
        ["34"]="Puducherry",["35"]="Andaman and Nicobar Islands",["36"]="Telangana",["37"]="Andhra Pradesh",["38"]="Ladakh"
    };
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static void State(string? code)
    {
        if (string.IsNullOrEmpty(code) || !States.ContainsKey(code)) throw new ValidationException("Choose a supported GST state/territory code.");
    }
    public static void Dates(DateTime from, DateTime? to)
    {
        if (from == default || from.TimeOfDay != TimeSpan.Zero || to?.TimeOfDay != null && to.Value.TimeOfDay != TimeSpan.Zero || to < from)
            throw new ValidationException("Enter valid effective dates; the end date must not precede the start date.");
    }
    public static bool ValidGstin(string value)
    {
        if (!Regex.IsMatch(value,"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$")) return false;
        const string alphabet="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var factor=2; var sum=0;
        for(var i=13;i>=0;i--) { var product=alphabet.IndexOf(value[i])*factor; sum+=product/36+product%36; factor=factor==2?1:2; }
        return alphabet[(36-sum%36)%36]==value[14];
    }
    public static void Gstin(string? gstin)
    {
        if(gstin is not null && !ValidGstin(gstin)) throw new ValidationException("GSTIN format or checksum is invalid. This check does not verify GST Portal registration.");
    }
    public static void Validate(GstBusiness b)
    {
        b.LegalName=b.LegalName.Trim(); b.Address=b.Address.Trim(); b.Pan=Clean(b.Pan)?.ToUpperInvariant(); b.Mobile=Clean(b.Mobile); b.Email=Clean(b.Email);
        Validator.ValidateObject(b,new(b),true); State(b.StateCode);
        if(!Regex.IsMatch(b.PinCode,"^[1-9][0-9]{5}$") || b.Pan is not null && !Regex.IsMatch(b.Pan,"^[A-Z]{5}[0-9]{4}[A-Z]$")) throw new ValidationException("Enter a valid PIN code and PAN where applicable.");
    }
    public static void Validate(GstRegistration r)
    {
        r.Gstin=Clean(r.Gstin)?.ToUpperInvariant(); r.Address=r.Address.Trim();
        Validator.ValidateObject(r,new(r),true); State(r.StateCode); Dates(r.EffectiveFrom,r.EffectiveTo); Gstin(r.Gstin);
        if(r.BusinessId<=0 || r.Status is not ("ACTIVE" or "INACTIVE") || r.RegistrationType is not ("REGULAR" or "COMPOSITION" or "UNREGISTERED" or "OTHER")) throw new ValidationException("Choose a legal business, registration type and status.");
        if((r.RegistrationType=="UNREGISTERED") != (r.Gstin is null) || r.Gstin is not null && !r.Gstin.StartsWith(r.StateCode,StringComparison.Ordinal)) throw new ValidationException("GSTIN must match the registration type and state code.");
        if(r.RegistrationType=="OTHER" && Clean(r.OtherType) is null) throw new ValidationException("Describe the alternative legal registration type.");
    }
    public static void Validate(GstMapping m)
    {
        Dates(m.EffectiveFrom,m.EffectiveTo);
        if(m.RegistrationId<=0 || m.StoreId<=0 || !BusinessProfiles.IsSupported(m.BusinessProfileCode) || m.Status is not ("ACTIVE" or "INACTIVE")) throw new ValidationException("Select a valid registration, shop profile, store and status.");
    }
    public static void Validate(GstTaxConfiguration t)
    {
        t.HsnSac=t.HsnSac.Trim(); t.Uqc=t.Uqc.Trim().ToUpperInvariant();
        Validator.ValidateObject(t,new(t),true); Dates(t.EffectiveFrom,t.EffectiveTo);
        if(t.ProductId<=0 || t.Classification is not ("GOODS" or "SERVICE") || !Regex.IsMatch(t.HsnSac,"^[0-9]{4}([0-9]{2})?([0-9]{2})?$") || t.Classification=="SERVICE" && (t.HsnSac.Length!=6 || !t.HsnSac.StartsWith("99",StringComparison.Ordinal))) throw new ValidationException("Choose a product and valid HSN/SAC format (service SAC: six digits starting 99). Official classification review is still required.");
        if(!Regex.IsMatch(t.Uqc,"^[A-Z]{3}$") || t.TaxRate is <0 or >100 || t.CessRate is <0 or >100 || decimal.Round(t.TaxRate,2)!=t.TaxRate || decimal.Round(t.CessRate,4)!=t.CessRate) throw new ValidationException("Enter a three-letter UQC and valid decimal tax rates. The existing POS stores GST rates to two decimal places.");
        if(t.Treatment is not ("TAXABLE" or "NIL" or "EXEMPT" or "NON_GST" or "ZERO_RATED") || t.Treatment!="TAXABLE" && t.TaxRate!=0) throw new ValidationException("Non-taxable treatments require a zero rate; zero-rated transactions need separate legal-context review.");
    }
}
