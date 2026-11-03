using System.ComponentModel.DataAnnotations;
namespace StockFlow.Application.Interfaces;

public interface IBarcodeManagementRepository
{
    Task<IReadOnlyList<BarcodeLotResponse>> ListAsync();
    Task<BarcodeWorkspace?> ReadAsync(string kind,long id);
    Task<BarcodeWorkspace?> PrepareAsync(string kind,long id,string[] keys,int userId);
    Task<BarcodeWorkspace?> PrintAsync(string kind,long id,BarcodePrintRequest request,int userId);
}
public sealed class BarcodeWorkspace
{
    public BarcodeLotResponse Lot { get; set; } = new();
    public IReadOnlyList<BarcodePiece> Rows { get; set; } = [];
    public IReadOnlyList<BarcodePrintHistory> History { get; set; } = [];
    public bool HistoryAvailable { get; set; }
}
public sealed class BarcodePiece
{
    public string Key { get; set; } = "";
    public long SourceItemId { get; set; }
    public int ItemNumber { get; set; }
    public long? UnitId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? VariantName { get; set; }
    public string? SerialNumber { get; set; }
    public string SerialSource { get; set; } = "LEGACY_UNKNOWN";
    public string? BarcodeSource { get; set; }
    public bool IsSerialTracked { get; set; }
    public string? Barcode { get; set; }
    public bool CanGenerate { get; set; } = true;
    public decimal SellingPrice { get; set; }
    public DateTime? LastRequestedAt { get; set; }
    public string? LastRequestedBy { get; set; }
}
public sealed class BarcodePrintHistory
{
    [System.Text.Json.Serialization.JsonIgnore] public string? TemplateJson { get; set; }
    public BarcodeSheetTemplate? SheetTemplate { get; set; }
    public bool SmallLabelScanTested { get; set; }
    public Guid RequestId { get; set; }
    public string RowKey { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public DateTime RequestedAt { get; set; }
    public string Format { get; set; } = "";
    public string Action { get; set; } = "";
    public int Copies { get; set; }
}
public sealed class BarcodePrintRequest
{
    public Guid RequestId { get; set; }
    [Required,RegularExpression("^(PRINT|REPRINT)$")] public string Action { get; set; } = "PRINT";
    [Required,RegularExpression("^(sheet|thermal|thermal50x30|thermal40x25|thermal30x20)$")] public string Format { get; set; } = "thermal50x30";
    public BarcodeSheetTemplate? SheetTemplate { get; set; }
    public bool SmallLabelScanTested { get; set; }
    [Required,MinLength(1),MaxLength(500)] public BarcodePrintSelection[] Items { get; set; } = [];
}
public sealed class BarcodeSheetTemplate
{
    public decimal WidthMm { get; set; } = 63;
    public decimal HeightMm { get; set; } = 38;
    public int Columns { get; set; } = 3;
    public int Rows { get; set; } = 7;
    public decimal LeftMm { get; set; } = 7.5m;
    public decimal TopMm { get; set; } = 15.5m;
    public decimal ColumnGapMm { get; set; } = 3;
    public decimal RowGapMm { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool Valid => WidthMm is >=20 and <=100 && HeightMm is >=15 and <=100 && Columns is >=1 and <=8 && Rows is >=1 and <=20
        && LeftMm is >=0 and <=50 && TopMm is >=0 and <=50 && ColumnGapMm is >=0 and <=20 && RowGapMm is >=0 and <=20
        && LeftMm+Columns*WidthMm+(Columns-1)*ColumnGapMm<=210 && TopMm+Rows*HeightMm+(Rows-1)*RowGapMm<=297;
}
public sealed class BarcodePrintSelection
{
    [Required,StringLength(100)] public string Key { get; set; } = "";
    [Required,StringLength(150)] public string Barcode { get; set; } = "";
    [Range(1,500)] public int Copies { get; set; } = 1;
}
