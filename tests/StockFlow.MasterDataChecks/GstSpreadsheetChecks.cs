using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using StockFlow.Web.Services;

namespace StockFlow.MasterDataChecks;

internal static class GstSpreadsheetChecks
{
    public static void Run()
    {
        void Reject(byte[] bytes,string name,string label)
        {try{GstMappingSpreadsheet.Read(bytes,name);}catch(Exception e)when(e is ValidationException or InvalidDataException){Console.WriteLine("PASS: "+label);return;}throw new Exception("Expected rejection: "+label);}
        byte[] Csv(string text)=>Encoding.UTF8.GetBytes(text);
        Reject(Csv("ProductId,HsnSac\n1,not-a-classification"),"missing.csv","missing RuleId header rejected");
        Reject(Csv("ProductId,RuleId,RuleId\n1,1,1"),"duplicate.csv","duplicate headers rejected");
        Reject(Csv("ProductId,RuleId,Unexpected\n1,1,x"),"unknown.csv","unexpected columns rejected");
        Reject(Csv("ProductId,RuleId\n\"unterminated"),"quoted.csv","malformed CSV quoting rejected");
        Reject(Csv("ProductId,RuleId\n"+string.Join('\n',Enumerable.Repeat("1,1",50001))),"oversized.csv","row limits enforced before preview");
        Reject(new byte[10*1024*1024+1],"oversized.xlsx","upload byte limit enforced");
        var source="ProductId,RuleId,ProductCode,ProductSku,EffectiveFrom\r\n1,2,0000123,0000456,\r\n";
        var parsed=GstMappingSpreadsheet.Read(GstMappingSpreadsheet.Xlsx(source,true),"text-identifiers.xlsx").Single();
        if(parsed.ProductCode!="0000123"||parsed.ProductSku!="0000456"||parsed.Error is not null)throw new Exception("Leading zero identifiers changed");Console.WriteLine("PASS: Excel preserves leading-zero codes and SKUs as text");
        var malformed=GstMappingSpreadsheet.Read(Csv("ProductId,RuleId,EffectiveFrom\n1,2,not-a-date"),"dates.csv").Single();if(malformed.Error is null)throw new Exception("Malformed date accepted");Console.WriteLine("PASS: invalid dates produce row errors");
        var formula=GstMappingSpreadsheet.Xlsx("ProductId,RuleId\n1,2\n");
        using(var stream=new MemoryStream())
        {
            stream.Write(formula);stream.Position=0;
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true))
            {
                var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;string xml;using(var reader=new StreamReader(entry.Open()))xml=reader.ReadToEnd();entry.Delete();using var writer=new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());writer.Write(xml.Replace("<is>","<f>1+1</f><is>"));
            }
            Reject(stream.ToArray(),"formula.xlsx","Excel formulas rejected without execution");
        }
        Console.WriteLine("GST spreadsheet safety checks passed.");
    }
}
