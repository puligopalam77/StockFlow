using System.ComponentModel.DataAnnotations;

namespace StockFlow.Application.Common;

public sealed class AddressComponents
{
    [StringLength(250)] public string? AddressLine1 { get; set; }
    [StringLength(250)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? State { get; set; }
    [StringLength(100)] public string? District { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? Country { get; set; } = "India";
    [StringLength(20)] public string? PostalCode { get; set; }

    public string? NormalizeAndFormat()
    {
        AddressLine1=SupplierRules.Clean(AddressLine1);AddressLine2=SupplierRules.Clean(AddressLine2);
        State=SupplierRules.Clean(State);District=SupplierRules.Clean(District);City=SupplierRules.Clean(City);
        Country=SupplierRules.Clean(Country);PostalCode=SupplierRules.Clean(PostalCode);
        Validator.ValidateObject(this,new ValidationContext(this),true);
        if(PostalCode is not null && (Country is null || Country.Equals("India",StringComparison.OrdinalIgnoreCase) || Country.Equals("IN",StringComparison.OrdinalIgnoreCase)) && !System.Text.RegularExpressions.Regex.IsMatch(PostalCode,"^[1-9][0-9]{5}$"))
            throw new ValidationException("Enter a valid 6-digit Indian PIN code.");
        var address=PostalAddress.Format(AddressLine1,AddressLine2,City,District,State,PostalCode);
        return address.Length==0?null:address+(Country is null?"":"\n"+Country);
    }
}
