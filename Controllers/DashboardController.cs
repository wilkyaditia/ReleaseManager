using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;

namespace ReleaseManager.Controllers;
[Authorize]
public class DashboardController : Controller
{
    private readonly AppDbContext _db; public DashboardController(AppDbContext db) => _db = db;
    public async Task<IActionResult> Index() => View(await _db.Releases.Include(x => x.ProgramInfo).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync());
    public IActionResult Error() => View();
}
