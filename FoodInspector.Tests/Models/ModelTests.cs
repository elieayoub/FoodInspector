using Xunit;
using FoodInspector.Models;

namespace FoodInspector.Tests.Models;

public class ModelTests
{
    // ── AppUser ──

    [Fact]
    public void AppUser_DefaultValues_AreCorrect()
    {
        var user = new AppUser();

        Assert.Equal(0, user.Id);
        Assert.Equal(string.Empty, user.Name);
        Assert.Equal(0, user.Age);
        Assert.True(user.CreatedAt <= DateTime.UtcNow);
        Assert.True(user.CreatedAt > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public void AppUser_SetProperties_RetainsValues()
    {
        var user = new AppUser { Id = 1, Name = "Alice", Age = 30 };

        Assert.Equal(1, user.Id);
        Assert.Equal("Alice", user.Name);
        Assert.Equal(30, user.Age);
    }

    // ── ScanResult ──

    [Fact]
    public void ScanResult_DefaultValues_AreCorrect()
    {
        var scan = new ScanResult();

        Assert.Equal(0, scan.Id);
        Assert.Equal(0, scan.UserId);
        Assert.Equal(string.Empty, scan.ExtractedText);
        Assert.Equal(string.Empty, scan.AnalysisJson);
        Assert.True(scan.ScannedAt <= DateTime.UtcNow);
        Assert.Null(scan.User);
    }

    // ── IngredientAnalysis ──

    [Fact]
    public void IngredientAnalysis_DefaultValues_AreCorrect()
    {
        var analysis = new IngredientAnalysis();

        Assert.Equal(string.Empty, analysis.OverallVerdict);
        Assert.Equal(string.Empty, analysis.Summary);
        Assert.NotNull(analysis.Ingredients);
        Assert.Empty(analysis.Ingredients);
    }

    // ── IngredientDetail ──

    [Fact]
    public void IngredientDetail_DefaultValues_AreCorrect()
    {
        var detail = new IngredientDetail();

        Assert.Equal(string.Empty, detail.Name);
        Assert.Equal(string.Empty, detail.Status);
        Assert.Equal(string.Empty, detail.Reason);
    }

    // ── RegisterViewModel ──

    [Fact]
    public void RegisterViewModel_DefaultValues_AreCorrect()
    {
        var vm = new RegisterViewModel();

        Assert.Equal(string.Empty, vm.Name);
        Assert.Equal(0, vm.Age);
    }

    // ── ScanViewModel ──

    [Fact]
    public void ScanViewModel_DefaultValues_AreCorrect()
    {
        var vm = new ScanViewModel();

        Assert.Null(vm.ImageBase64);
        Assert.Null(vm.ExtractedText);
        Assert.Null(vm.Analysis);
        Assert.Equal(string.Empty, vm.UserName);
        Assert.Equal(0, vm.UserAge);
    }

    // ── ErrorViewModel ──

    [Fact]
    public void ErrorViewModel_ShowRequestId_TrueWhenSet()
    {
        var vm = new ErrorViewModel { RequestId = "abc123" };
        Assert.True(vm.ShowRequestId);
    }

    [Fact]
    public void ErrorViewModel_ShowRequestId_FalseWhenNull()
    {
        var vm = new ErrorViewModel { RequestId = null };
        Assert.False(vm.ShowRequestId);
    }

    [Fact]
    public void ErrorViewModel_ShowRequestId_FalseWhenEmpty()
    {
        var vm = new ErrorViewModel { RequestId = "" };
        Assert.False(vm.ShowRequestId);
    }
}
