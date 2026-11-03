using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.DTOs.Invoices;

public sealed class GstInvoiceDetails
{
    public string RegistrationType { get; set; } = "";
    public string PlaceOfSupply { get; set; } = "";
    public string? DeliveryAddress { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Utgst { get; set; }
    public decimal Igst { get; set; }
    public decimal Cess { get; set; }
    public List<GstLine> Lines { get; set; } = [];
    public string[] Warnings { get; set; } = [];
    public bool UnregisteredRecipient { get; set; }
    public string DocumentTitle => RegistrationType=="COMPOSITION" || RegistrationType=="REGULAR" && Lines.All(l=>l.Treatment is "NIL" or "EXEMPT" or "NON_GST") ? "Bill of Supply"
        : RegistrationType=="UNREGISTERED"?"Invoice":UnregisteredRecipient && Lines.Any(l=>l.Treatment is "NIL" or "EXEMPT" or "NON_GST")?"Invoice-cum-Bill of Supply":"Tax Invoice";
}
