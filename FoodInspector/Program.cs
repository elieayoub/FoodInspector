using Microsoft.EntityFrameworkCore;
using FoodInspector.Data;
using FoodInspector.Services;

var builder = WebApplication.CreateBuilder(args);

// Allow large image uploads (up to 20 MB)
builder.WebHost.ConfigureKestrel(opt => opt.Limits.MaxRequestBodySize = 20 * 1024 * 1024);

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=foodinspector.db"));

// Services
builder.Services.AddSingleton<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IIngredientAnalyzer, OpenAiIngredientAnalyzer>();
builder.Services.AddSingleton<IDailyIntakeService, DailyIntakeService>();
builder.Services.AddHttpClient();

// Session (stores logged-in user)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromHours(24);
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Auto-migrate database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
