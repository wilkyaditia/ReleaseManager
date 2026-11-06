using ReleaseManager.Data;
using ReleaseManager.Models;
using System.Security.Claims;

namespace ReleaseManager.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;
    public AuditService(AppDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }
    public async Task LogAsync(string action, string? entity = null, int? entityId = null, string? description = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Username = _http.HttpContext?.User?.Identity?.Name,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            Description = description,
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }
}
