using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Web.Services;

// A deliberately narrow data-only XLSX/CSV interchange: no macros, formulas, links or product creation.
public static class GstMappingSpreadsheet
{
    public const string Header="ProductId,VariantId,VariantSku,RuleId,HsnSac,Treatment,TaxRate,EffectiveFrom,EffectiveTo,ReviewReference";
    private static readonly XNamespace Sheet="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static List<GstImportRow> Read(byte[] bytes,string name)
    {
        if(bytes.Length==0||bytes.Length>10*1024*1024)throw new ValidationException("Upload a non-empty CSV or XLSX file up to 10 MB.");
        var table=Path.GetExtension(name).ToLowerInvariant() switch{".csv"=>ReadCsv(new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff')),".xlsx"=>ReadXlsx(bytes),_=>throw new ValidationException("Use a .csv or .xlsx file.")};
        if(table.Count<2||table.Count>50001)throw new ValidationException("Import between 1 and 50,000 data rows.");
        var header=table[0].Select(h=>h.Trim()).ToArray();
        var allowed=(GstImportRow.TemplateHeader+",ParentCategory,Subcategory,Brand,MappingStatus").Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(header.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=header.Length||!new[]{"ProductId","RuleId"}.All(h=>header.Contains(h,StringComparer.OrdinalIgnoreCase))||header.Any(h=>!allowed.Contains(h)))throw new ValidationException("Use the downloaded template's unique headers, including ProductId and RuleId. Unknown columns are not accepted.");
        var result=new List<GstImportRow>();
        for(var i=1;i<table.Count;i++)
        {
            var values=table[i];string? Get(string key){var index=Array.FindIndex(header,h=>h.Equals(key,StringComparison.OrdinalIgnoreCase));return index<0||index>=values.Length?null:Null(values[index]);}
            var row=new GstImportRow{Row=i+1};result.Add(row);
            try
            {
                if(values.Length>header.Length)throw new FormatException("Unexpected extra columns.");
                row.ProductId=int.Parse(Get("ProductId")??"",CultureInfo.InvariantCulture);row.VariantId=Get("VariantId") is {} variant?long.Parse(variant,CultureInfo.InvariantCulture):null;row.VariantSku=Get("VariantSku");row.RuleId=long.Parse(Get("RuleId")??"",CultureInfo.InvariantCulture);
                row.ProductCode=Get("ProductCode");row.ProductSku=Get("ProductSku");
                row.EffectiveFrom=Get("EffectiveFrom") is {} start?ParseDate(start):default;row.EffectiveTo=Get("EffectiveTo") is {} end?ParseDate(end):null;row.HsnSac=Get("HsnSac");row.Treatment=Get("Treatment");row.ReviewReference=Get("ReviewReference");row.TaxRate=Get("TaxRate") is {} rate?decimal.Parse(rate,CultureInfo.InvariantCulture):null;
                row.PricesIncludeTax=Get("PricesIncludeTax") is {} pricing?bool.Parse(pricing):null;
                if(row.ProductId<=0||row.RuleId<=0||row.VariantId<=0)throw new FormatException("Positive IDs required.");
            }
            catch(Exception e) when(e is FormatException or OverflowException or ArgumentException){row.Error="Malformed row. Use positive stable IDs, numeric rates and yyyy-MM-dd dates.";}
        }
        return result;
    }
    private static string? Null(string value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static DateTime ParseDate(string? value)=>DateTime.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial)&&serial is >=1 and <=2958465?DateTime.FromOADate(serial).Date:throw new FormatException("Invalid date.");
    public static List<string[]> ReadCsv(string text)
    {
        var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();bool quote=false,closed=false;
        void Field(){row.Add(field.ToString());field.Clear();closed=false;if(row.Count>50)throw new ValidationException("At most 50 columns are allowed.");}
        void Row(){Field();if(row.Any(v=>v.Length>0))rows.Add(row.ToArray());row.Clear();if(rows.Count>50001)throw new ValidationException("At most 50,000 data rows are allowed.");}
        for(var i=0;i<text.Length;i++)
        {
            var ch=text[i];if(quote){if(ch=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else{quote=false;closed=true;}}else field.Append(ch);}
            else if(ch=='"'&&field.Length==0&&!closed)quote=true;
            else if(ch==',')Field();else if(ch is '\r' or '\n'){if(ch=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;Row();}
            else{if(closed||ch=='"')throw new ValidationException("Malformed CSV quoting.");field.Append(ch);}
            if(field.Length>4000)throw new ValidationException("Spreadsheet cells are limited to 4,000 characters.");
        }
        if(quote)throw new ValidationException("CSV has an unterminated quoted field.");if(field.Length>0||row.Count>0||closed)Row();return rows;
    }
    private static List<string[]> ReadXlsx(byte[] bytes)
    {
        using var input=new MemoryStream(bytes);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        if(zip.Entries.Count>100||zip.Entries.Sum(e=>e.Length)>50*1024*1024||zip.Entries.Any(e=>e.FullName.Contains("vbaProject",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("externalLinks",StringComparison.OrdinalIgnoreCase)))throw new ValidationException("Workbook is too large or contains unsupported executable/external content.");
        XDocument? Read(string path){var entry=zip.GetEntry(path);if(entry is null)return null;using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50*1024*1024});return XDocument.Load(reader);}
        var shared=Read("xl/sharedStrings.xml")?.Descendants(Sheet+"si").Select(si=>string.Concat(si.Descendants(Sheet+"t").Select(t=>t.Value))).ToArray()??[];
        var sheet=Read("xl/worksheets/sheet1.xml")??throw new ValidationException("Use a standard data workbook with the template in its first sheet.");
        var rows=new List<string[]>();foreach(var source in sheet.Descendants(Sheet+"row"))
        {
            var values=new string[50];var last=0;foreach(var cell in source.Elements(Sheet+"c"))
            {
                if(cell.Element(Sheet+"f") is not null)throw new ValidationException("Import values only; spreadsheet formulas are not accepted.");
                var reference=(string?)cell.Attribute("r")??throw new ValidationException("Cell reference missing.");var index=0;foreach(var c in reference.TakeWhile(char.IsLetter))index=index*26+char.ToUpperInvariant(c)-'A'+1;
                if(index<1||index>50)throw new ValidationException("Use at most 50 columns.");var type=(string?)cell.Attribute("t");var value=type=="inlineStr"?string.Concat(cell.Descendants(Sheet+"t").Select(t=>t.Value)):cell.Element(Sheet+"v")?.Value??"";
                if(type=="s"){if(!int.TryParse(value,out var n)||n<0||n>=shared.Length)throw new ValidationException("Invalid shared string index.");value=shared[n];}
                if(value.Length>4000)throw new ValidationException("Cell is too long.");values[index-1]=value;last=Math.Max(last,index);
            }
            if(last>0)rows.Add(values.Take(last).Select(v=>v??"").ToArray());if(rows.Count>50001)throw new ValidationException("Use at most 50,000 data rows.");
        }
        return rows;
    }
    public static byte[] Xlsx(string csv,bool instructions=false)
    {
        var rows=ReadCsv(csv.TrimStart('\ufeff'));using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            void Add(string path,string content){using var writer=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false));writer.Write(content);}
            Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"+(instructions?"<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>":"")+"</Types>");
            Add("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Tax Mappings\" sheetId=\"1\" r:id=\"rId1\"/>"+(instructions?"<sheet name=\"Instructions\" sheetId=\"2\" r:id=\"rId2\"/>":"")+"</sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>"+(instructions?"<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>":"")+"</Relationships>");
            var sheet=new XElement(Sheet+"worksheet",new XElement(Sheet+"sheetData",rows.Select((row,i)=>new XElement(Sheet+"row",new XAttribute("r",i+1),row.Select((value,j)=>new XElement(Sheet+"c",new XAttribute("r",Column(j+1)+(i+1)),new XAttribute("t","inlineStr"),new XElement(Sheet+"is",new XElement(Sheet+"t",value))))))));Add("xl/worksheets/sheet1.xml",sheet.ToString(SaveOptions.DisableFormatting));
            if(instructions)
            {
                var notes=new[]{"Complete the Tax Mappings sheet. Keep ProductId, VariantId, ProductCode, ProductSku and VariantSku unchanged.","Remove rows you do not want to update. Each product/variant may appear only once.","Fill RuleId using an active REVIEWED Tax Rule from this shop. Do not infer tax from category or HSN alone.","HSN/SAC, treatment, rate, pricing and review reference may be left blank to use the reviewed rule. Any supplied value must match that rule.","Blank EffectiveFrom uses today or the rule start, whichever is later. Blank EffectiveTo uses the reviewed rule end. Explicit dates use yyyy-MM-dd.","Product defaults and sellable variants can be imported together using their stable IDs. No product or SKU will be created or renamed.","Validate the file, inspect every warning/conflict, then confirm import. Only READY rows are applied; other rows are skipped and reported.","Maximum 50,000 data rows and 10 MB. Values only; formulas, macros and external workbook links are not accepted.","No sample HSN or percentage is supplied: classifications must be reviewed for the actual goods/services and effective period."};
                var info=new XElement(Sheet+"worksheet",new XElement(Sheet+"cols",new XElement(Sheet+"col",new XAttribute("min",1),new XAttribute("max",1),new XAttribute("width",130),new XAttribute("customWidth",1))),new XElement(Sheet+"sheetData",notes.Select((text,i)=>new XElement(Sheet+"row",new XAttribute("r",i+1),new XElement(Sheet+"c",new XAttribute("r","A"+(i+1)),new XAttribute("t","inlineStr"),new XElement(Sheet+"is",new XElement(Sheet+"t",text)))))));
                Add("xl/worksheets/sheet2.xml",info.ToString(SaveOptions.DisableFormatting));
            }
        }
        return output.ToArray();
    }
    private static string Column(int n)=>n<=26?((char)('A'+n-1)).ToString():Column((n-1)/26)+(char)('A'+(n-1)%26);
}
