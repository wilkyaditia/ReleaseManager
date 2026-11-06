using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ReleaseManager.Data;
using ReleaseManager.Models;
using ReleaseManager.Services;
using ReleaseManager.ViewModels;

namespace ReleaseManager.Controllers;

[Authorize(Roles = UserRoles.Admin)]
public class UsersController : Controller
{
    private readonly AppDbContext _db;
    private readonly PasswordService _passwords;
    private readonly AuditService _audit;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AppDbContext db, PasswordService passwords, AuditService audit, ILogger<UsersController> logger)
    {
        _db = db;
        _passwords = passwords;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> Index() =>
        View(await _db.Users.OrderBy(x => x.Username).ToListAsync());

    public IActionResult Create()
    {
        LoadRoles();
        return View(new RegisterUserViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RegisterUserViewModel model)
    {
        model.Username = model.Username?.Trim() ?? string.Empty;
        model.DisplayName = model.DisplayName?.Trim() ?? string.Empty;

        if (!UserRoles.All.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
        if (ModelState.IsValid && await _db.Users.AnyAsync(x => x.Username == model.Username))
            ModelState.AddModelError(nameof(model.Username), "That username is already in use.");

        if (!ModelState.IsValid)
        {
            LoadRoles();
            return View(model);
        }

        var user = new AppUser
        {
            Username = model.Username,
            DisplayName = model.DisplayName,
            Role = model.Role,
            PasswordHash = _passwords.Hash(model.Password),
            Active = true
        };

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to create user {Username}", model.Username);
            ModelState.AddModelError(string.Empty, "The account could not be saved. Check that the username is unique.");
            LoadRoles();
            return View(model);
        }

        await _audit.LogAsync("CREATE_USER", "User", user.Id, user.Username);
        TempData["Success"] = "Account created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();

        LoadRoles();
        return View(new EditUserViewModel
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            Active = user.Active
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model)
    {
        var user = await _db.Users.FindAsync(model.Id);
        if (user is null) return NotFound();

        model.Username = model.Username?.Trim() ?? string.Empty;
        model.DisplayName = model.DisplayName?.Trim() ?? string.Empty;

        if (!UserRoles.All.Contains(model.Role))
            ModelState.AddModelError(nameof(model.Role), "Select a valid role.");
        if (!string.IsNullOrEmpty(model.NewPassword) && model.NewPassword != model.ConfirmPassword)
            ModelState.AddModelError(nameof(model.ConfirmPassword), "The passwords do not match.");
        if (ModelState.IsValid && await _db.Users.AnyAsync(x => x.Id != model.Id && x.Username == model.Username))
            ModelState.AddModelError(nameof(model.Username), "That username is already in use.");

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isCurrentUser = currentUserId == user.Id.ToString() ||
                            (currentUserId is null && string.Equals(User.Identity?.Name, user.Username, StringComparison.OrdinalIgnoreCase));
        if (isCurrentUser && (model.Role != UserRoles.Admin || !model.Active))
            ModelState.AddModelError(string.Empty, "You cannot deactivate or demote your own account.");

        var isRemovingActiveAdmin = user.Active && user.Role == UserRoles.Admin &&
                                    (!model.Active || model.Role != UserRoles.Admin);
        if (isRemovingActiveAdmin && await _db.Users.CountAsync(x => x.Active && x.Role == UserRoles.Admin) <= 1)
            ModelState.AddModelError(string.Empty, "At least one active Admin account must remain.");

        if (!ModelState.IsValid)
        {
            LoadRoles();
            return View(model);
        }

        user.Username = model.Username;
        user.DisplayName = model.DisplayName;
        user.Role = model.Role;
        user.Active = model.Active;
        if (!string.IsNullOrEmpty(model.NewPassword))
            user.PasswordHash = _passwords.Hash(model.NewPassword);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to update user {UserId}", user.Id);
            ModelState.AddModelError(string.Empty, "The account could not be saved. Check that the username is unique.");
            LoadRoles();
            return View(model);
        }

        await _audit.LogAsync("EDIT_USER", "User", user.Id, user.Username);
        if (isCurrentUser)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("DisplayName", user.DisplayName)
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }
        TempData["Success"] = "Account updated.";
        return RedirectToAction(nameof(Index));
    }

    private void LoadRoles() => ViewBag.Roles = UserRoles.All;
}
