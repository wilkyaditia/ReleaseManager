using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Services;
using ReleaseManager.ViewModels;

namespace ReleaseManager.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db; private readonly PasswordService _password; private readonly AuditService _audit;
    public AccountController(AppDbContext db, PasswordService password, AuditService audit) { _db = db; _password = password; _audit = audit; }
    [AllowAnonymous] public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel());
    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Username == model.Username && x.Active);
        if (user == null || !_password.Verify(model.Password, user.PasswordHash)) { ModelState.AddModelError("", "Username or password is invalid."); return View(model); }
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role), new Claim("DisplayName", user.DisplayName) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        await _audit.LogAsync("LOGIN", "User", user.Id);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Index", "Dashboard");
    }
    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout() { await _audit.LogAsync("LOGOUT"); await HttpContext.SignOutAsync(); return RedirectToAction(nameof(Login)); }
    [AllowAnonymous] public IActionResult Denied() => Content("Access denied.");
}
