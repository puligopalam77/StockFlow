using System.ComponentModel.DataAnnotations;
using StockFlow.Application.Common;
using StockFlow.Application.DTOs.Purchases;
using StockFlow.Application.Interfaces;

namespace StockFlow.Application.Services;

public class PurchaseService : IPurchaseService
{
    private readonly IPurchaseRepository repository;
    private readonly RetailVariantSelection? selection;
    // ASP.NET supplies these services when it creates this class.
    public PurchaseService(IPurchaseRepository repository, RetailVariantSelection? selection = null)
    {
        this.repository = repository;
        this.selection = selection;
    }

    public async Task<long> CreateAsync(CreatePurchaseRequest request, int userId)
    {
        ValidateUser(userId);
        PurchaseRules.ValidateAndCalculate(request);
        if(selection is not null)await selection.ValidateAsync(request.Items.Select(i=>(i.ProductId,i.VariantId,(long?)null)));
        return await repository.CreatePurchaseAsync(request, userId);
    }

    public async Task<bool> UpdateDraftAsync(long id, CreatePurchaseRequest request, int userId)
    {
        ValidateUser(userId);
        PurchaseRules.ValidateAndCalculate(request);
        if(selection is not null)await selection.ValidateAsync(request.Items.Select(i=>(i.ProductId,i.VariantId,(long?)null)));
        return await repository.UpdateDraftAsync(id, request, userId);
    }

    public Task<bool> CompleteAsync(long id, int userId)
    {
        ValidateUser(userId);
        return repository.CompleteAsync(id, userId);
    }

    public Task<IEnumerable<PurchaseResponse>> GetAllAsync()
    {
        return repository.GetAllAsync();
    }

    public Task<PurchaseResponse?> GetByIdAsync(long id)
    {
        return repository.GetByIdAsync(id);
    }

    public Task<PurchaseAttachment?> GetAttachmentAsync(long id)
    {
        return repository.GetAttachmentAsync(id);
    }

    private static void ValidateUser(int id)
    {
        if (id <= 0)
        {
            throw new ValidationException("A valid signed-in user is required.");
        }
    }
}
