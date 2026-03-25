using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FoodInspector.Data;
using FoodInspector.Models;

namespace FoodInspector.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;

    public AccountController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Register()
    {
        // If already registered, redirect to scanner
        if (HttpContext.Session.GetInt32("UserId") != null)
            return RedirectToAction("Index", "Scan");

        return View(new RegisterViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            ModelState.AddModelError(nameof(model.Name), "Name is required.");

        if (model.Age < 1 || model.Age > 120)
            ModelState.AddModelError(nameof(model.Age), "Please enter a valid age (1–120).");

        if (!ModelState.IsValid)
            return View(model);

        var user = new AppUser { Name = model.Name.Trim(), Age = model.Age };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        HttpContext.Session.SetInt32("UserId", user.Id);
        HttpContext.Session.SetString("UserName", user.Name);
        HttpContext.Session.SetInt32("UserAge", user.Age);

        return RedirectToAction("Index", "Scan");
    }

    [HttpPost]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Register");
    }
}
