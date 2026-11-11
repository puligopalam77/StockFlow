using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Operations;

namespace StockFlow.Infrastructure.Repositories;

// Existing payment rows remain authoritative. Each UNION arm identifies one source event;
// no trigger, background job or duplicate payment-to-ledger write is needed.
internal static class CashReadModel
{
    internal const string MovementsSql = """
        WITH opening AS (
          SELECT adjustment_id opening_id,start_date,(to_jsonb(a)->>'tracking_started_at')::timestamptz tracking_started_at,
            to_jsonb(a)->'source_cutoffs' source_cutoffs FROM cash_adjustments a WHERE kind='OPENING' AND location_id=@Location
        ), reviews AS ( /*REVIEWS*/ ), activity AS (
          SELECT 'SALE:'||p.payment_id id,p.payment_date AT TIME ZONE current_setting('TimeZone') original_occurred,
            s.invoice_number::text reference,'SALE'::text type,'Cash sale / customer collection'::text description,p.amount,
            NULL::uuid adjustment_id,false can_reverse,'SALES'::text source_module,'/Sales/Details/'||s.sale_id source_url,p.payment_id source_number,false date_only
          FROM payments p JOIN sales s ON s.sale_id=p.sale_id AND s.tenant_id=p.tenant_id
          WHERE p.location_id=@Location AND p.payment_mode='CASH'
          UNION ALL SELECT 'SERVICE:'||service_payment_id,payment_date,'SRV-'||service_payment_id,'SERVICE','Repair payment',amount,NULL::uuid,false,'SERVICE','/Service/Details/'||service_request_id,service_payment_id,false
            FROM service_payments WHERE location_id=@Location AND payment_mode='CASH'
          UNION ALL SELECT 'REFUND:'||sale_return_id,created_at,return_number,'REFUND','Cash refunded to customer',-refund_amount,NULL::uuid,false,'SALES','/Sales/Details/'||sale_id,sale_return_id,false
            FROM sale_returns WHERE location_id=@Location AND refund_mode='CASH' AND refund_amount>0
          UNION ALL SELECT 'SUPPLIER:'||supplier_payment_id,payment_date,'SUP-'||supplier_payment_id,'SUPPLIER',coalesce(nullif(notes,''),'Cash paid to supplier'),-amount,NULL::uuid,false,'PURCHASES','/Purchases/Details/'||purchase_id,supplier_payment_id,false
            FROM supplier_payments WHERE location_id=@Location AND payment_mode='CASH'
          UNION ALL SELECT 'EXPENSE:'||m.id,m.occurred,m.reference,'EXPENSE',m.description,-m.amount,NULL::uuid,false,'EXPENSES','/Expenses/Details?id='||p.expense_id,m.id,true
            FROM sf_cash_expenses(@Location,@Epoch,@End,@Zone) m
            LEFT JOIN expense_payments p ON p.movement_id=m.id AND p.location_id=@Location
          UNION ALL SELECT 'ADJUSTMENT:'||a.adjustment_id,
            CASE WHEN a.kind='OPENING' THEN coalesce((to_jsonb(a)->>'tracking_started_at')::timestamptz,a.start_date::timestamp AT TIME ZONE @Zone) ELSE a.created_at END,
            CASE WHEN a.kind='OPENING' THEN 'OPN-' ELSE 'ADJ-' END||lpad(a.movement_number::text,greatest(4,length(a.movement_number::text)),'0'),a.kind,a.reason||coalesce(' · '||nullif(a.request_payload->>'note',''),''),a.amount,a.adjustment_id,
            a.kind NOT IN('OPENING','REVERSAL') AND NOT EXISTS(SELECT 1 FROM cash_adjustments r WHERE r.reverses_id=a.adjustment_id),NULL::text,NULL::text,NULL::bigint,false
            FROM cash_adjustments a WHERE a.location_id=@Location
        ), review_candidates AS (
          SELECT a.*,r.timing,coalesce(r.version,0) review_version FROM activity a CROSS JOIN opening o
          LEFT JOIN reviews r ON r.opening_id=o.opening_id AND r.movement_id=a.source_number
          WHERE o.tracking_started_at IS NOT NULL AND o.tracking_started_at<@End AND a.type='EXPENSE'
            AND a.source_number>coalesce((o.source_cutoffs->>'EXPENSE')::bigint,0)
            AND (a.original_occurred AT TIME ZONE @Zone)::date=o.start_date
        ), tracked AS (
          SELECT a.id,CASE WHEN o.tracking_started_at IS NOT NULL THEN greatest(a.original_occurred,o.tracking_started_at) ELSE a.original_occurred END occurred,
            a.reference,a.type,a.description,a.amount,a.adjustment_id,a.can_reverse,a.source_module,a.source_url,a.source_number,a.date_only
          FROM activity a CROSS JOIN opening o
          LEFT JOIN reviews r ON r.opening_id=o.opening_id AND r.movement_id=a.source_number AND a.type='EXPENSE'
          WHERE a.original_occurred<@End AND (
            o.tracking_started_at IS NULL AND a.original_occurred>=o.start_date::timestamp AT TIME ZONE @Zone
            OR o.tracking_started_at IS NOT NULL AND o.tracking_started_at<@End AND (
              a.type='OPENING' OR a.adjustment_id IS NOT NULL AND a.original_occurred>=o.tracking_started_at
              OR a.source_number>coalesce((o.source_cutoffs->>a.type)::bigint,0) AND (
                a.type<>'EXPENSE'
                OR (a.original_occurred AT TIME ZONE @Zone)::date>o.start_date
                OR (a.original_occurred AT TIME ZONE @Zone)::date=o.start_date AND r.timing='AFTER'
              )
            )
          )
        )
        """;

    internal static object Parameters(BusinessScope scope, ShopTimeOptions time, DateTime date, string? search = null, int page = 1, string? type = null, string? direction = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZoneId);
        DateTime Utc(DateTime day) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), zone);
        return new { Location=scope.LocationId, Zone=time.TimeZoneId, Epoch=Utc(new DateTime(2000,1,1)), Start=Utc(date), End=Utc(date.AddDays(1)), Search=search?.Trim() ?? "", Offset=(page-1)*20, Type=type ?? "", Direction=direction ?? "" };
    }

    internal static async Task<bool> Available(IDbConnection c, IDbTransaction t) => await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.cash_adjustments') IS NOT NULL",transaction:t);

    internal static async Task<string> Sql(IDbConnection c,IDbTransaction t)
    {
        var hasReviews=await c.ExecuteScalarAsync<bool>("SELECT to_regclass('public.cash_opening_expense_reviews') IS NOT NULL",transaction:t);
        return MovementsSql.Replace("/*REVIEWS*/",hasReviews
            ? "SELECT DISTINCT ON(opening_id,movement_id) opening_id,movement_id,timing,version FROM cash_opening_expense_reviews ORDER BY opening_id,movement_id,version DESC"
            : "SELECT NULL::uuid opening_id,NULL::bigint movement_id,NULL::text timing,0::bigint version WHERE false");
    }

    internal static async Task<Summary> Read(IDbConnection c, IDbTransaction t, object parameters)
    {
        var summary=await c.QuerySingleAsync<Summary>(await Sql(c,t)+"""
            SELECT (SELECT start_date FROM opening) TrackingStart,
              (SELECT tracking_started_at FROM opening) TrackingStartedAt,
              (SELECT count(*) FROM review_candidates WHERE timing IS NULL) PendingReviewCount,
              coalesce(sum(amount),0) Balance,
              coalesce(sum(amount) FILTER(WHERE occurred<@Start OR type='OPENING'),0) OpeningCash,
              coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND amount>0 AND type<>'OPENING'),0) Received,
              -coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND amount<0 AND type<>'OPENING'),0) Paid,
              coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND type='SALE'),0) SalesCash,
              coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND type='SERVICE'),0) ServiceCash,
              -coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND type='REFUND'),0) CashRefunds,
              -coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND type='SUPPLIER'),0) SupplierCash,
              -coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND type='EXPENSE'),0) ExpenseCash,
              coalesce(sum(amount) FILTER(WHERE occurred>=@Start AND adjustment_id IS NOT NULL AND type<>'OPENING'),0) AdjustmentCash,
              count(*) FILTER(WHERE occurred>=@Start) MovementCount,
              (SELECT count(DISTINCT p.sale_id) FROM payments p JOIN tracked m ON m.id='SALE:'||p.payment_id WHERE m.occurred>=@Start) SalesCount,
              count(*) FILTER(WHERE occurred>=@Start AND type='SERVICE') ServiceCount,
              count(*) FILTER(WHERE occurred>=@Start AND type='REFUND') RefundCount,
              count(*) FILTER(WHERE occurred>=@Start AND type='SUPPLIER') SupplierCount,
              count(*) FILTER(WHERE occurred>=@Start AND type='EXPENSE') ExpenseCount,
              coalesce(jsonb_agg(jsonb_build_array(id,amount,occurred) ORDER BY id)::text,'[]') Fingerprint
            FROM tracked
            """,parameters,t);
        summary.Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summary.Fingerprint)));
        return summary;
    }

    internal sealed class Summary : CashTotals
    {
        public DateTime? TrackingStart { get; set; }
        public DateTimeOffset? TrackingStartedAt { get; set; }
        public decimal Balance { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal Received { get; set; }
        public decimal Paid { get; set; }
    }
}
