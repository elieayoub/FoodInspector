using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using FoodInspector.Controllers;
using FoodInspector.Models;
using FoodInspector.Tests.Helpers;

namespace FoodInspector.Tests.Controllers;

public class AccountControllerTests
{
    // ── GET Register ──

    [Fact]
    public void Register_Get_NoSession_ReturnsView()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var result = controller.Register();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<RegisterViewModel>(viewResult.Model);
    }

    [Fact]
    public void Register_Get_WithSession_RedirectsToScan()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var result = controller.Register();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Scan", redirect.ControllerName);
    }

    // ── POST Register ──

    [Fact]
    public async Task Register_Post_ValidModel_CreatesUserAndRedirects()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Bob", Email = "bob@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = 30 };
        var result = await controller.Register(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Scan", redirect.ControllerName);

        // User persisted
        var user = Assert.Single(db.Users);
        Assert.Equal("Bob", user.Name);
        Assert.Equal("bob@example.com", user.Email);
        Assert.Equal(30, user.Age);

        // Session set
        Assert.Equal(user.Id, controller.HttpContext.Session.GetInt32("UserId"));
        Assert.Equal("Bob", controller.HttpContext.Session.GetString("UserName"));
        Assert.Equal(30, controller.HttpContext.Session.GetInt32("UserAge"));
    }

    [Fact]
    public async Task Register_Post_EmptyName_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "", Email = "test@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey("Name"));
    }

    [Fact]
    public async Task Register_Post_WhitespaceName_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "   ", Email = "test@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Name"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    [InlineData(999)]
    public async Task Register_Post_InvalidAge_ReturnsViewWithError(int age)
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Test", Email = "test@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = age };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Age"));
    }

    [Fact]
    public async Task Register_Post_NameIsTrimmed()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "  Alice  ", Email = "alice@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = 25 };
        await controller.Register(model);

        var user = Assert.Single(db.Users);
        Assert.Equal("Alice", user.Name);
    }

    // ── POST Logout ──

    [Fact]
    public void Logout_ClearsSessionAndRedirects()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var result = controller.Logout();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Null(controller.HttpContext.Session.GetInt32("UserId"));
    }

    // ── GET Login ──

    [Fact]
    public void Login_Get_NoSession_ReturnsView()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var result = controller.Login();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<LoginViewModel>(viewResult.Model);
    }

    [Fact]
    public void Login_Get_WithSession_RedirectsToScan()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller, userId: 1, userName: "Alice", userAge: 25);

        var result = controller.Login();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Scan", redirect.ControllerName);
    }

    // ── POST Login ──

    [Fact]
    public async Task Login_Post_ValidCredentials_RedirectsToScan()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        // Register a user first
        db.Users.Add(new AppUser
        {
            Name = "Alice",
            Email = "alice@example.com",
            PasswordHash = AccountController.HashPassword("secret123"),
            Age = 25
        });
        await db.SaveChangesAsync();

        var model = new LoginViewModel { Email = "alice@example.com", Password = "secret123" };
        var result = await controller.Login(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Scan", redirect.ControllerName);
        Assert.NotNull(controller.HttpContext.Session.GetInt32("UserId"));
    }

    [Fact]
    public async Task Login_Post_WrongPassword_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        db.Users.Add(new AppUser
        {
            Name = "Alice",
            Email = "alice@example.com",
            PasswordHash = AccountController.HashPassword("secret123"),
            Age = 25
        });
        await db.SaveChangesAsync();

        var model = new LoginViewModel { Email = "alice@example.com", Password = "wrongpassword" };
        var result = await controller.Login(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Login_Post_NonExistentEmail_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new LoginViewModel { Email = "nobody@example.com", Password = "secret123" };
        var result = await controller.Login(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Login_Post_EmptyEmail_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new LoginViewModel { Email = "", Password = "secret123" };
        var result = await controller.Login(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Email"));
    }

    [Fact]
    public async Task Login_Post_EmptyPassword_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new LoginViewModel { Email = "alice@example.com", Password = "" };
        var result = await controller.Login(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Password"));
    }

    [Fact]
    public async Task Login_Post_SetsSessionCorrectly()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        db.Users.Add(new AppUser
        {
            Name = "Bob",
            Email = "bob@example.com",
            PasswordHash = AccountController.HashPassword("mypassword"),
            Age = 30
        });
        await db.SaveChangesAsync();

        var model = new LoginViewModel { Email = "bob@example.com", Password = "mypassword" };
        await controller.Login(model);

        Assert.Equal("Bob", controller.HttpContext.Session.GetString("UserName"));
        Assert.Equal(30, controller.HttpContext.Session.GetInt32("UserAge"));
    }

    // ── Registration Validation ──

    [Fact]
    public async Task Register_Post_EmptyEmail_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Alice", Email = "", Password = "secret123", ConfirmPassword = "secret123", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Email"));
    }

    [Fact]
    public async Task Register_Post_EmptyPassword_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Alice", Email = "alice@example.com", Password = "", ConfirmPassword = "", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Password"));
    }

    [Fact]
    public async Task Register_Post_ShortPassword_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Alice", Email = "alice@example.com", Password = "abc", ConfirmPassword = "abc", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Password"));
    }

    [Fact]
    public async Task Register_Post_PasswordMismatch_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        var model = new RegisterViewModel { Name = "Alice", Email = "alice@example.com", Password = "secret123", ConfirmPassword = "different", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("ConfirmPassword"));
    }

    [Fact]
    public async Task Register_Post_DuplicateEmail_ReturnsViewWithError()
    {
        using var db = DbHelper.CreateInMemoryContext();
        var controller = new AccountController(db);
        SessionHelper.SetupSession(controller);

        db.Users.Add(new AppUser
        {
            Name = "Existing",
            Email = "alice@example.com",
            PasswordHash = AccountController.HashPassword("old"),
            Age = 30
        });
        await db.SaveChangesAsync();

        var model = new RegisterViewModel { Name = "Alice", Email = "alice@example.com", Password = "secret123", ConfirmPassword = "secret123", Age = 25 };
        var result = await controller.Register(model);

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Email"));
    }

    [Fact]
    public void HashPassword_SameInput_ReturnsSameHash()
    {
        var hash1 = AccountController.HashPassword("test123");
        var hash2 = AccountController.HashPassword("test123");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashPassword_DifferentInput_ReturnsDifferentHash()
    {
        var hash1 = AccountController.HashPassword("test123");
        var hash2 = AccountController.HashPassword("other456");

        Assert.NotEqual(hash1, hash2);
    }
}
