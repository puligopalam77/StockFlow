using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Security;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;

namespace StockFlow.MasterDataChecks;

sealed class FakeBusinessProfileRepository(IBranchRepository branches, IProductRepository products, ICategoryRepository categories, IHttpContextAccessor http) : IBusinessProfileRepository
{
    public int Transactions { get; set; }
    public int SaveCalls { get; private set; }
    public async Task<BusinessProfileImpact> ImpactAsync() => new()
    {
        ProductCount = products is FakeProductRepository fake?fake.AllRows.Count:(await products.GetAllAsync()).Count(), CategoryCount = (await categories.GetAllAsync()).Count(), TransactionCount = Transactions
    };
    public async Task<long> ChangeAsync(BusinessProfileChange request)
    {
        var state = ((FakeBranchRepository)branches).State;
        if (request.ExpectedVersion != state.BusinessProfileVersion) throw new ValidationException("Profile version changed.");
        var impact = await ImpactAsync();
        var retained=state.ProfileCatalogSwitchingAvailable&&BusinessProfiles.IsDemoProfile(state.BusinessProfileCode)&&BusinessProfiles.IsDemoProfile(request.Code);
        if (request.Code != state.BusinessProfileCode && !retained && (impact.ProductCount > 0 || impact.TransactionCount > 0)) throw new ValidationException("Existing catalog blocks profile change.");
        if (request.Code == state.BusinessProfileCode) return state.BusinessProfileVersion;
        state.BusinessProfileCode = request.Code; state.BusinessProfileVersion++; SaveCalls++;
        http.HttpContext!.RequestServices.GetRequiredService<BusinessScope>().BusinessProfileCode = request.Code;
        if (request.CreateStarterCategories) foreach (var name in BusinessProfiles.StarterCategories(request.Code))
            if (!(await categories.GetAllAsync()).Any(c => c.CategoryName == name)) await categories.CreateAsync(new Category { CategoryName = name, IsActive = true });
        return state.BusinessProfileVersion;
    }
}
