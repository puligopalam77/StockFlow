using StockFlow.Application.DTOs.Gst;

namespace StockFlow.Application.Interfaces;

public interface IGstRepository
{
    Task<GstSettings> SettingsAsync(bool catalog = true);
    Task<GstCatalogPage> CatalogAsync(GstCatalogQuery query);
    Task<GstCatalogOptions> CatalogOptionsAsync();
    Task<GstRulePage> TaxRulesAsync(GstCatalogQuery query);
    Task<long> SaveRuleAsync(GstTaxRule rule);
    Task ReviewRuleAsync(long ruleId);
    Task EndRuleAsync(GstRuleEnd request);
    Task<GstMappingHistory> MappingHistoryAsync(int productId,long? variantId);
    Task<GstOperation> PreviewAssignmentAsync(GstAssignment request);
    Task<GstOperation> OperationAsync(Guid id,int page=1,int pageSize=25,string? status=null);
    Task<GstOperationPage> OperationsAsync(int page=1,int pageSize=10);
    Task<GstOperation> CommitAssignmentAsync(GstOperationConfirm request);
    Task<GstExportResult> ExportMappingsAsync(GstCatalogQuery query,List<GstTarget>? targets=null);
    Task<GstExportResult> ImportTemplateAsync(GstCatalogQuery query);
    Task<GstSetupReadiness> SetupReadinessAsync(long registrationId);
    Task<GstSetupReadiness> ActivateSetupAsync(GstActivateSetup request);
    Task<GstAuditPage> AuditAsync(GstAuditQuery query);
    Task<long> SaveBusinessAsync(GstBusiness business);
    Task<long> SaveRegistrationAsync(GstRegistration registration);
    Task<long> SaveMappingAsync(GstMapping mapping);
    Task<long> SaveTaxAsync(GstTaxConfiguration tax);
    Task<IEnumerable<GstRegistration>> RegistrationsAsync();
    Task<GstReport> ReportAsync(GstReportQuery query, bool export=false);
    Task<GstDocument?> DocumentAsync(long id);
    Task<long> RecordAsync(GstRecordRequest request);
    Task ReviewAsync(GstReviewRequest request);
    Task<GstExportResult> ExportAsync(GstReportQuery query);
    Task<GstSourceOptions> SourceOptionsAsync();
    Task<IEnumerable<GstExportHistory>> ExportHistoryAsync();
    Task<IEnumerable<GstReviewHistory>> ReviewHistoryAsync(long id);
}
