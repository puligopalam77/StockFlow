namespace StockFlow.Application.DTOs.Suppliers;

public class UpdateSupplierRequest : CreateSupplierRequest
{
    public bool IsActive { get; set; } = true;
}
