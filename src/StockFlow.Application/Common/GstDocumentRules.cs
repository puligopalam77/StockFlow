using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.Common;

public static class GstDocumentRules
{
    public static void Query(GstReportQuery q)
    {
        if(q.RegistrationId<=0 || q.From==default || q.To==default || q.From.TimeOfDay!=TimeSpan.Zero || q.To.TimeOfDay!=TimeSpan.Zero || q.To<q.From || (q.To-q.From).TotalDays>3660)
            throw new ValidationException("Select a GST registration and valid date range (maximum ten years).");
        q.Term=GstRules.Clean(q.Term);
        if(q.Page<1 || q.Page>1000000 || !new[]{10,25,50,100}.Contains(q.Size) || q.Term?.Length>200 || q.Report is not ("sales" or "purchases" or "notes" or "hsn") || q.Sort is not ("date" or "number" or "value") || q.PartyType is not (null or "B2B" or "B2C") || q.TaxRate is <0 or >100)
            throw new ValidationException("Choose supported report, search, sort and pagination values.");
    }
    public static void Record(GstRecordRequest r)
    {
        Validator.ValidateObject(r,new(r),true);GstRules.Dates(r.DocumentDate,null);GstRules.State(r.PlaceOfSupply);
        if(r.DocumentDate>TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).Date)throw new ValidationException("A source tax document cannot be dated in the future.");
        if(r.RequestId==Guid.Empty || r.RegistrationId<=0 || r.Direction is not ("SALES" or "PURCHASE") || r.Kind is not ("INVOICE" or "CREDIT_NOTE" or "DEBIT_NOTE") || r.Kind=="INVOICE" && r.Direction!="PURCHASE") throw new ValidationException("Choose a supported source document. Sales invoices come only from completed POS sales.");
        r.DocumentNumber=r.DocumentNumber.Trim();
        if(r.Direction=="SALES" && !Regex.IsMatch(r.DocumentNumber,"^[A-Za-z0-9/-]{1,16}$")) throw new ValidationException("Sales tax note numbers must follow the statutory serial format.");
        if(r.Kind!="INVOICE" && (r.ParentDocumentId is null or <=0 || string.IsNullOrWhiteSpace(r.Reason))) throw new ValidationException("A tax adjustment needs its original invoice and reason; a refund alone is not a tax note.");
        if(r.Kind=="INVOICE" && (r.SupplierId is null or <=0 || r.PurchaseIds.Count==0 || r.PurchaseIds.Count>100 || r.PurchaseIds.Distinct().Count()!=r.PurchaseIds.Count)) throw new ValidationException("Link the actual supplier and distinct related purchase records; do not record each GRN as a separate invoice.");
        if(r.Lines.Count is <1 or >200)throw new ValidationException("Provide 1–200 supplier invoice or adjustment lines.");
        foreach(var line in r.Lines)
        {
            Validator.ValidateObject(line,new(line),true);
            if(line.IsManual && (line.ProductId is not null || line.SourceLineId is null or <=0 || r.Direction!="SALES" || r.Kind=="INVOICE"))
                throw new ValidationException("Manual tax adjustments must link their original POS invoice line.");
            if(line.Classification=="SERVICE" && (line.HsnSac.Length!=6 || !line.HsnSac.StartsWith("99",StringComparison.Ordinal)))throw new ValidationException("Service SAC must contain six digits starting with 99.");
            if((line.ProductId is null or <=0 && !(line.IsManual && line.SourceLineId>0 && r.Direction=="SALES" && r.Kind!="INVOICE")) || line.Quantity<=0 || line.Quantity>1000000 || !Regex.IsMatch(line.HsnSac,"^[0-9]{4}([0-9]{2})?([0-9]{2})?$") || !Regex.IsMatch(line.Uqc,"^[A-Z]{3}$") || line.Classification is not ("GOODS" or "SERVICE") || line.Treatment is not ("TAXABLE" or "NIL" or "EXEMPT" or "NON_GST" or "ZERO_RATED") || line.TaxRate is <0 or >100) throw new ValidationException("Check product, quantity, HSN/SAC, UQC and tax classification on every line.");
            foreach(var amount in new[]{line.TaxableValue,line.Cgst,line.Sgst,line.Utgst,line.Igst,line.Cess,line.Total}) if(amount<0 || amount>9999999999m || decimal.Round(amount,2)!=amount) throw new ValidationException("Tax amounts must be non-negative and have at most two decimal places.");
            if(line.Total!=line.TaxableValue+line.Cgst+line.Sgst+line.Utgst+line.Igst+line.Cess || line.Igst>0 && line.Cgst+line.Sgst+line.Utgst>0 || line.Sgst>0 && line.Utgst>0) throw new ValidationException("Tax components must reconcile with each recorded line total.");
            if(line.Treatment!="TAXABLE" && line.Cgst+line.Sgst+line.Utgst+line.Igst>0) throw new ValidationException("Non-taxable lines cannot contain collected GST components.");
            if(Math.Abs(line.Cgst+line.Sgst+line.Utgst+line.Igst-decimal.Round(line.TaxableValue*line.TaxRate/100,2,MidpointRounding.AwayFromZero))>0.01m) throw new ValidationException("Recorded supplier tax does not reconcile with its rate and taxable value.");
        }
    }
}
