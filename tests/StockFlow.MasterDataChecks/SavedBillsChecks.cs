namespace StockFlow.MasterDataChecks;
// The saved-bills API now includes leases and explicit transitions. Exercise the current contract.
internal static class SavedBillsChecks
{
    public static Task Run()=>DraftLifecycleChecks.Run();
}
