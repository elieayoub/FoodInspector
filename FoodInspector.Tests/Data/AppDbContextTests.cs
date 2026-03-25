using Xunit;
using Microsoft.EntityFrameworkCore;
using FoodInspector.Models;
using FoodInspector.Tests.Helpers;

namespace FoodInspector.Tests.Data;

public class AppDbContextTests
{
    [Fact]
    public async Task CanAddAndRetrieveUser()
    {
        using var db = DbHelper.CreateInMemoryContext();

        db.Users.Add(new AppUser { Name = "Alice", Age = 30 });
        await db.SaveChangesAsync();

        var user = await db.Users.FirstAsync();
        Assert.Equal("Alice", user.Name);
        Assert.Equal(30, user.Age);
        Assert.True(user.Id > 0);
    }

    [Fact]
    public async Task CanAddAndRetrieveScanResult()
    {
        using var db = DbHelper.CreateInMemoryContext();

        var user = new AppUser { Name = "Bob", Age = 25 };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.ScanResults.Add(new ScanResult
        {
            UserId = user.Id,
            ExtractedText = "sugar, salt",
            AnalysisJson = "{\"OverallVerdict\":\"Buy\"}"
        });
        await db.SaveChangesAsync();

        var scan = await db.ScanResults.Include(s => s.User).FirstAsync();
        Assert.Equal("sugar, salt", scan.ExtractedText);
        Assert.Equal(user.Id, scan.UserId);
        Assert.NotNull(scan.User);
        Assert.Equal("Bob", scan.User!.Name);
    }

    [Fact]
    public async Task ScanResult_BelongsToUser_ViaForeignKey()
    {
        using var db = DbHelper.CreateInMemoryContext();

        var user = new AppUser { Name = "Carol", Age = 40 };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var scan1 = new ScanResult { UserId = user.Id, ExtractedText = "flour", AnalysisJson = "{}" };
        var scan2 = new ScanResult { UserId = user.Id, ExtractedText = "sugar", AnalysisJson = "{}" };
        db.ScanResults.AddRange(scan1, scan2);
        await db.SaveChangesAsync();

        var scans = await db.ScanResults.Where(s => s.UserId == user.Id).ToListAsync();
        Assert.Equal(2, scans.Count);
    }

    [Fact]
    public async Task MultipleUsers_AreIndependent()
    {
        using var db = DbHelper.CreateInMemoryContext();

        db.Users.Add(new AppUser { Name = "User1", Age = 20 });
        db.Users.Add(new AppUser { Name = "User2", Age = 30 });
        await db.SaveChangesAsync();

        var users = await db.Users.ToListAsync();
        Assert.Equal(2, users.Count);
        Assert.NotEqual(users[0].Id, users[1].Id);
    }
}
