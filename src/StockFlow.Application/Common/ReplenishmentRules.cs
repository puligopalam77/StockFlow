using StockFlow.Application.DTOs.Inventory;
namespace StockFlow.Application.Common;
public static class ReplenishmentRules
{
 public static void Calculate(ReplenishmentRow r,int days)
 {
  r.DailyDemand=Math.Max(0,r.Sold-r.RestockedReturns)/(decimal)days;
  r.Evidence=r.Sold==0?"No sales history":r.HistoryDays<days||r.SaleDays<3?"Limited history":"Observed history";
  r.Position=r.Stock+r.OpenPo+r.IncomingTransfer;
  if(r.RestockedReturns>r.Sold)r.Warnings.Add("Restocked returns exceed sales in this window; net demand is floored at zero.");
  if(r.NonSaleableReturns>0)r.Warnings.Add("Non-saleable returns do not reduce demand or count as incoming stock.");
  if(r.IsSerialTracked&&r.SerialUnits!=r.Stock)r.Warnings.Add("Ledger and available serial counts differ; review before ordering.");
  if(r.Stock<0)r.Warnings.Add("Negative ledger balance requires review.");
  r.Warnings.Add("Historical averages do not correct for stockout days, spikes or seasonality. Committed outgoing supply is not modelled.");
  r.Warnings.Add("Incoming quantities have no verified arrival dates; drafts are excluded. Observed supplier lead time is unavailable without a confirmed order-placement event.");
  var p=r.Policy;
  if(p is null){r.Target=r.Minimum;r.RawSuggestion=Math.Max(0,r.Minimum-r.Position);r.Suggested=0;r.Status="SUPPLIER_NOT_CONFIGURED";r.Warnings.Add("No approved policy: minimum-only gap shown; no purchase quantity is proposed.");return;}
  if(r.DailyDemand>0&&r.Evidence=="Observed history")r.CoverDays=Math.Round(Math.Max(0,r.Stock)/r.DailyDemand,1);
  r.ReorderPoint=r.DailyDemand*p.LeadDays+p.SafetyStock;
  r.Target=Math.Max(r.Minimum,r.DailyDemand*(p.LeadDays+p.CoverageDays)+p.SafetyStock);
  r.RawSuggestion=(long)Math.Max(0,decimal.Ceiling(r.Target-r.Position));
  var suggestion=r.RawSuggestion==0?0:decimal.Ceiling(Math.Max(r.RawSuggestion,p.Moq)/(decimal)p.OrderMultiple)*p.OrderMultiple;
  if(suggestion>int.MaxValue){r.Suggested=0;r.Status="DATA_WARNING";r.Warnings.Add("Suggested quantity exceeds supported integer order size.");return;}
  r.Suggested=(int)suggestion;
  if(r.Suggested>r.RawSuggestion)r.Warnings.Add("MOQ/order multiple increases the raw suggestion.");
  r.Status=r.Stock<0||r.IsSerialTracked&&r.SerialUnits!=r.Stock?"DATA_WARNING":r.DailyDemand==0||r.Evidence!="Observed history"?"INSUFFICIENT_HISTORY":r.Position>=r.Target&&r.Stock<r.Target?"COVERED":r.Position<=r.ReorderPoint?"REORDER_NOW":r.Suggested>0?"REVIEW_SOON":"SUFFICIENT";
  if(r.Evidence!="Observed history")r.Warnings.Add("Limited/no history: recommendation is advisory; no stockout date or precise cover is shown.");
 }
}
