using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using cafe_yo.Services;
using cafe_yo.Services.Payments;
using cafe_yo.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<cafe_yo.Data.ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<cafe_yo.Models.ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<cafe_yo.Data.ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin, "admin"));
    options.AddPolicy("OwnerOnly", p => p.RequireRole(AppRoles.Owner, "owner"));
    options.AddPolicy("SupervisorOnly", p => p.RequireRole(AppRoles.Supervisor, "supervisor"));
    options.AddPolicy("KasirOnly", p => p.RequireRole(AppRoles.Kasir, "kasir"));
    options.AddPolicy("KokiOnly", p => p.RequireRole(AppRoles.Koki, AppRoles.DapurLegacy, "koki", "dapur"));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/staff";
    options.AccessDeniedPath = "/forbidden";
});

builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.Configure<BayarGgOptions>(builder.Configuration.GetSection(BayarGgOptions.SectionName));
builder.Services.AddHttpClient<IBayarGgClient, BayarGgClient>((sp, client) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BayarGgOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds <= 0 ? 30 : options.TimeoutSeconds);
});

var app = builder.Build();

await cafe_yo.Data.IdentitySeeder.SeedAsync(app.Services);
await cafe_yo.Data.LegacyUsersSeeder.EnsureAsync(app.Services); // Ensure legacy users table exists
await cafe_yo.Data.OperationalSchemaInitializer.EnsureAsync(app.Services); // Ensure kitchen/order operational schema exists

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

