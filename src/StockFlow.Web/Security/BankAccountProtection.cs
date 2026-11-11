using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StockFlow.Application.DTOs.Operations;
namespace StockFlow.Web.Security;

public sealed class BankAccountProtection(IDataProtectionProvider provider,IOptions<BankAccountsOptions> options)
{
    // Plaintext never enters repository SQL, bank_requests, audit JSON or logs.
    public void Protect(JsonObject body,long tenant,Guid account)
    {
        string Text(string field)=>body[field] is null?"":body[field] is JsonValue value&&value.TryGetValue<string>(out var text)?text.Trim():throw new ValidationException("Enter valid bank account text fields.");
        foreach(var field in new[]{"protectedNumber","protectedUpi","fingerprint","lastFour","upiFingerprint"})body.Remove(field);
        foreach(var field in new[]{"bankName","displayName","holderName","accountType","ifsc"})
            body[field]=Text(field);
        var ifsc=body["ifsc"]!.GetValue<string>().ToUpperInvariant();body["ifsc"]=ifsc;
        if(!Regex.IsMatch(ifsc,"^[A-Z]{4}0[A-Z0-9]{6}$"))throw new ValidationException("Enter a valid IFSC code.");
        var number=Text("accountNumber");
        var upi=Text("upiId");
        var replaceUpi=body.ContainsKey("upiId");
        body.Remove("accountNumber");body.Remove("upiId");
        if(upi.Length>150||upi.Length>0&&!Regex.IsMatch(upi,"^[A-Za-z0-9._-]{2,}@[A-Za-z0-9.-]{2,}$"))throw new ValidationException("Enter a valid UPI ID.");
        byte[] key;
        try{key=Convert.FromBase64String(options.Value.IdentityKey);}catch(FormatException){throw new ValidationException("Bank identity key is not configured.");}
        if(key.Length<32)throw new ValidationException("A durable bank identity key is required.");
        string Fingerprint(string value)=>Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var protector=provider.CreateProtector("StockFlow.BankAccount.v1",tenant.ToString(),account.ToString());
        if(number.Length>0){
            if(!Regex.IsMatch(number,"^[0-9]{6,34}$"))throw new ValidationException("Use 6–34 digits for the account number.");
            body["lastFour"]=number[^4..];body["fingerprint"]=Fingerprint($"{tenant}:{ifsc[..4]}:{number}");
            body["protectedNumber"]=protector.Protect(number);
        }
        if(replaceUpi){body["upiFingerprint"]=Fingerprint(upi);body["protectedUpi"]=upi.Length==0?null:protector.Protect(upi);}
    }
}
