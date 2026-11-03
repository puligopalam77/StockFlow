using System.ComponentModel.DataAnnotations;
using StockFlow.Application.DTOs.Products;
using StockFlow.Domain.Entities;

namespace StockFlow.Application.Services;

public static class CategoryFieldInheritance
{
    public static List<CategoryAttributeRule> Fields(CatalogSchema schema, int categoryId) =>
        (schema.EffectiveCategories ?? schema.Categories).SingleOrDefault(c => c.CategoryId == categoryId)?.Fields ?? [];

    public static void Resolve(CatalogSchema schema, IEnumerable<Category> categories)
    {
        var all = categories.ToDictionary(c => c.CategoryId);
        schema.EffectiveCategories = [];
        foreach (var category in all.Values)
        {
            var fields = new Dictionary<string, CategoryAttributeRule>();
            var seen = new HashSet<int>();
            Category? current = category;
            while (current is not null && seen.Add(current.CategoryId))
            {
                foreach (var rule in schema.Categories.SingleOrDefault(c => c.CategoryId == current.CategoryId)?.Fields ?? [])
                    fields.TryAdd(rule.Code, new() { Code=rule.Code, Required=rule.Required, VariantAxis=rule.VariantAxis,
                        Searchable=rule.Searchable, Visible=rule.Visible, DefaultValue=rule.DefaultValue, DefinedOnCategoryId=current.CategoryId });
                if (!current.InheritsParentFields || current.ParentCategoryId is not int parent) break;
                current = all.GetValueOrDefault(parent);
                if (current?.BusinessProfileCode != category.BusinessProfileCode) break;
            }
            if (fields.Count > 30 || fields.Values.Count(f => f.VariantAxis) > 3)
                throw new ValidationException($"{category.CategoryName}: inherited fields exceed 30 fields or three variant axes.");
            schema.EffectiveCategories.Add(new() { CategoryId=category.CategoryId, Fields=fields.Values.OrderBy(f=>f.Code).ToList() });
        }
    }
}
