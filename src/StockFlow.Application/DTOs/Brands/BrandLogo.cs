using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.DTOs.Brands;

public sealed class BrandLogo
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Data { get; set; } = "";
    public byte[] Validate()
    {
        if(Data.Length>2700000||FileName.Length is <1 or >255||FileName.Any(char.IsControl))throw new ValidationException("Choose a logo with a valid file name, up to 2 MB.");
        byte[] data;try{data=Convert.FromBase64String(Data);}catch(FormatException){throw new ValidationException("Invalid brand logo upload.");}
        var valid=ContentType switch{
            "image/png"=>data.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
            "image/jpeg"=>data.AsSpan().StartsWith(new byte[]{255,216,255}),
            "image/webp"=>data.Length>=12&&data.AsSpan(0,4).SequenceEqual("RIFF"u8)&&data.AsSpan(8,4).SequenceEqual("WEBP"u8),_=>false};
        if(data.Length is <1 or >2000000||!valid)throw new ValidationException("Upload a valid JPG, PNG or WebP logo up to 2 MB.");
        return data;
    }
}
