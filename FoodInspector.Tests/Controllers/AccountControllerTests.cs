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

        var model = new RegisterViewModel { Name = "Bob", Age = 30 };
        var result = await controller.Register(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Scan", redirect.ControllerName);

        // User persisted
        var user = Assert.Single(db.Users);
        Assert.Equal("Bob", user.Name);
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

        var model = new RegisterViewModel { Name = "", Age = 25 };
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

        var model = new RegisterViewModel { Name = "   ", Age = 25 };
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

        var model = new RegisterViewModel { Name = "Test", Age = age };
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

        var model = new RegisterViewModel { Name = "  Alice  ", Age = 25 };
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
        Assert.Equal("Register", redirect.ActionName);
        Assert.Null(controller.HttpContext.Session.GetInt32("UserId"));
    }
}
