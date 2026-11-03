using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Dapper;
using StockFlow.Application.DTOs.Operations;
using StockFlow.Application.Interfaces;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class ScreenLanguageRepository : IScreenLanguageRepository
{
    private readonly DbConnectionFactory factory;
    private bool? available;
    public ScreenLanguageRepository(DbConnectionFactory factory)
    {
        this.factory = factory;
    }

    public async Task<List<ScreenLanguageSetting>> GetAsync()
    {
        // Older databases keep English captions until the language migration is applied.
        if (!await IsAvailableAsync()) return new List<ScreenLanguageSetting>();
        await using var connection = factory.CreateConnection();
        var rows = await connection.QueryAsync<string>("SELECT setting::text FROM public.screen_language_settings ORDER BY screen");
        return rows.Select(row => JsonSerializer.Deserialize<ScreenLanguageSetting>(row)!).ToList();
    }

    public async Task SaveAsync(ScreenLanguageSetting setting)
    {
        Validate(setting);
        if (!await IsAvailableAsync())
            throw new ValidationException("Screen language setup is incomplete. Apply migration 036 after 035 to the application's database before saving language settings.");
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var old = await connection.QuerySingleOrDefaultAsync<string>("SELECT setting::text FROM public.screen_language_settings WHERE screen=@Screen FOR UPDATE", setting, transaction);
        var json = JsonSerializer.Serialize(setting);
        await connection.ExecuteAsync("""
            INSERT INTO public.screen_language_settings(screen,setting,updated_by) VALUES(@Screen,@Json::jsonb,@User)
            ON CONFLICT(tenant_id,screen) DO UPDATE SET setting=excluded.setting,updated_by=excluded.updated_by,updated_at=now();
            INSERT INTO audit_logs(user_id,module_name,action_name,table_name,record_id,old_values,new_values)
            VALUES(@User,'Screen languages','UPDATE','screen_language_settings',0,@Old::jsonb,@Json::jsonb);
            """, new
        {
            setting.Screen,
            Json = json,
            Old = old,
            User = factory.Scope!.UserId
        }, transaction);
        await transaction.CommitAsync();
    }

    public async Task<bool> IsAvailableAsync()
    {
        // Cache only for this request. A new request sees a newly applied migration.
        if (available.HasValue) return available.Value;
        await using var connection = factory.CreateConnection();
        available = await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.screen_language_settings') IS NOT NULL");
        return available.Value;
    }

    public static void Validate(ScreenLanguageSetting setting)
    {
        if (setting.Screen is null || !System.Text.RegularExpressions.Regex.IsMatch(setting.Screen, "^[a-z][a-z0-9-]{0,49}$"))
            throw new ValidationException("Choose a valid screen.");
        var languages = new[] { "en", "as", "bn", "brx", "doi", "gu", "hi", "kn", "ks", "kok", "mai", "ml", "mni", "mr", "ne", "or", "pa", "sa", "sat", "sd", "ta", "te", "ur" };
        if (!languages.Contains(setting.Language))
            throw new ValidationException("Choose a supported language.");
        if (setting.Captions is null || setting.Captions.Count > 500 || setting.Captions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 1000 || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 2000))
            throw new ValidationException("Enter both the English caption and its translation. Keep each caption within the displayed limits.");
        if (setting.Screen != "application" && !new[] { "en", "te", "hi" }.Contains(setting.Language) && !setting.Captions.Keys.Any(key => !key.StartsWith("validation.")))
            throw new ValidationException("Enter at least one screen caption translation for this language.");
        if (setting.Captions.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != setting.Captions.Count)
            throw new ValidationException("Each English caption must appear only once.");
        foreach (var pair in setting.Captions.Where(pair => pair.Key.StartsWith("validation.")))
        {
            if (!pair.Value.Contains("{label}") || (new[] { "validation.maximum", "validation.minimum", "validation.lower", "validation.upper", "validation.step" }.Contains(pair.Key) && !pair.Value.Contains("{limit}")))
                throw new ValidationException("Keep {label} and, where required, {limit} in validation translations.");
        }
    }
}
