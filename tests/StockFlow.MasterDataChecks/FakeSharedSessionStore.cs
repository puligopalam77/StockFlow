using Microsoft.Extensions.Options;
using StockFlow.Web.Security;

namespace StockFlow.MasterDataChecks;

// Test-only shared store; production always uses PostgreSQL.
internal sealed class FakeSharedSessionStore(TimeProvider clock,IOptions<SessionSecurityOptions> options) : ISharedSessionStore
{
    private sealed record Entry(SessionOwner Owner,string Id,string Ip,string Device,DateTimeOffset Created)
    {
        public DateTimeOffset Activity=Created,Idle,Absolute;
        public string? Reason;
    }
    private readonly Dictionary<string,Entry> sessions=new();
    private readonly Dictionary<string,(DateTimeOffset Start,int Count)> limits=new();
    public readonly List<(string Action,string Reason,string Reference)> Events=[];
    public bool Unavailable {get;set;}
    private string Normalize(string id)=>Guid.Parse(id).ToString("N").ToUpperInvariant();
    private void Audit(Entry entry,string action,string reason)=>Events.Add((action,reason,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.Id)))));
    public Task<SessionStatus> CreateAsync(SessionOwner owner,string id,string ip,string device)
    {
        lock(sessions)
        {
            var entry=new Entry(owner,Normalize(id),ip,device,clock.GetUtcNow())
            {Idle=clock.GetUtcNow().AddMinutes(options.Value.EffectiveIdleMinutes),Absolute=clock.GetUtcNow().AddHours(options.Value.AbsoluteHours)};
            sessions[entry.Id]=entry;Audit(entry,"SESSION_STARTED","LOGIN");return Task.FromResult(Status(entry));
        }
    }
    private SessionStatus Status(Entry entry)
    {
        var now=clock.GetUtcNow();
        if(entry.Reason is null && (entry.Idle<=now || entry.Absolute<=now))
        {entry.Reason=entry.Absolute<=now?"ABSOLUTE_TIMEOUT":"IDLE_TIMEOUT";Audit(entry,"SESSION_ENDED",entry.Reason);}
        return entry.Reason is null?new((long)(entry.Idle-now).TotalMilliseconds,(long)(entry.Absolute-now).TotalMilliseconds):new(0,0,entry.Reason);
    }
    public Task<SessionStatus> ValidateAsync(SessionOwner owner,string id,long? activityAgeMilliseconds=null)
    {
        if(Unavailable)throw new Npgsql.NpgsqlException("Simulated shared session outage.");
        lock(sessions)
        {
            if(!sessions.TryGetValue(Normalize(id),out var entry) || entry.Owner!=owner)return Task.FromResult(new SessionStatus(0,0,"SESSION_ENDED"));
            if(Status(entry).Active && activityAgeMilliseconds.HasValue)
            {
                var activity=clock.GetUtcNow().AddMilliseconds(-activityAgeMilliseconds.Value);
                if(activity>entry.Activity)entry.Activity=activity;
                var deadline=activity.AddMinutes(options.Value.EffectiveIdleMinutes);
                if(deadline>entry.Idle)entry.Idle=deadline<entry.Absolute?deadline:entry.Absolute;
            }
            return Task.FromResult(Status(entry));
        }
    }
    public Task<bool> RevokeAsync(SessionOwner owner,string id,string reason)
    {
        lock(sessions)
        {
            if(!Guid.TryParse(id,out _) || !sessions.TryGetValue(Normalize(id),out var entry) || entry.Owner!=owner || !Status(entry).Active)return Task.FromResult(false);
            entry.Reason=reason;Audit(entry,"SESSION_ENDED",reason);return Task.FromResult(true);
        }
    }
    public Task<IReadOnlyList<LoginSession>> ListAsync(SessionOwner owner)
    {
        lock(sessions)return Task.FromResult<IReadOnlyList<LoginSession>>(sessions.Values.Where(entry=>entry.Owner==owner && Status(entry).Active)
            .Select(entry=>new LoginSession(entry.Id,entry.Device,entry.Ip,entry.Created,entry.Activity,entry.Absolute)).ToArray());
    }
    public Task<int> TakeLoginAttemptAsync(string bucket,int limit,int seconds)
    {
        lock(limits)
        {
            var now=clock.GetUtcNow();
            if(!limits.TryGetValue(bucket,out var entry) || entry.Start.AddSeconds(seconds)<=now)entry=(now,0);
            if(entry.Count>=limit)return Task.FromResult((int)Math.Ceiling((entry.Start.AddSeconds(seconds)-now).TotalSeconds));
            limits[bucket]=(entry.Start,entry.Count+1);return Task.FromResult(0);
        }
    }
    public Task SweepAsync(){lock(sessions){foreach(var entry in sessions.Values)Status(entry);}return Task.CompletedTask;}
}
