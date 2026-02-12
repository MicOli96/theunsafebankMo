using Microsoft.EntityFrameworkCore;
using theunsafebank.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<BankContext>(options =>
    options.UseSqlite("Data Source=bank.db"));

// Add session support (insecure - no encryption, just basic storage)
// builder.Services.AddDistributedMemoryCache(); // In-memory cache for sessions
// builder.Services.AddSession(options =>
// {
//     options.Cookie.HttpOnly = false; // INSECURE!
//     options.Cookie.IsEssential = true;
// });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
// if (!app.Environment.IsDevelopment())
// {
//     app.UseExceptionHandler("/Home/Error");
// }

// INSECURE!
// app.UseHttpsRedirection();

app.UseRouting();
// app.UseSession(); // INSECURE: Session is not properly configured, and not used in controllers
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}")
    .WithStaticAssets();


app.Run();
