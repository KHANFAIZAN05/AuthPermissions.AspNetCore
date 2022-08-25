// Copyright (c) 2022 Jon P Smith, GitHub: JonPSmith, web: http://www.thereformedprogrammer.net/
// Licensed under MIT license. See License.txt in the project root for license information.

using AuthPermissions;
using AuthPermissions.AspNetCore;
using AuthPermissions.AspNetCore.Services;
using AuthPermissions.AspNetCore.StartupServices;
using AuthPermissions.BaseCode;
using AuthPermissions.BaseCode.SetupCode;
using AuthPermissions.SupportCode;
using AuthPermissions.SupportCode.DownStatusCode;
using AuthPermissions.SupportCode.ShardingServices;
using Example6.MvcWebApp.Sharding.Data;
using Example6.MvcWebApp.Sharding.PermissionsCode;
using Example6.SingleLevelSharding.AppStart;
using Example6.SingleLevelSharding.EfCoreCode;
using Example6.SingleLevelSharding.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Net.DistributedFileStoreCache;
using RunMethodsSequentially;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
        options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

builder.Services.RegisterAuthPermissions<Example6Permissions>(options =>
{
    options.TenantType = TenantTypes.SingleLevel | TenantTypes.AddSharding;
    options.EncryptionKey = builder.Configuration[nameof(AuthPermissionsOptions.EncryptionKey)];
    options.PathToFolderToLock = builder.Environment.WebRootPath;
    options.Configuration = builder.Configuration;
})
    //NOTE: This uses the same database as the individual accounts DB
    .UsingEfCoreSqlServer(connectionString)
    .IndividualAccountsAuthentication()
    .RegisterAddClaimToUser<AddTenantNameClaim>()
    .RegisterTenantChangeService<ShardingTenantChangeService>()
    .AddRolesPermissionsIfEmpty(Example6AppAuthSetupData.RolesDefinition)
    .AddTenantsIfEmpty(Example6AppAuthSetupData.TenantDefinition)
    .AddAuthUsersIfEmpty(Example6AppAuthSetupData.UsersRolesDefinition)
    .RegisterFindUserInfoService<IndividualAccountUserLookup>()
    .RegisterAuthenticationProviderReader<SyncIndividualAccountUsers>()
    .AddSuperUserToIndividualAccounts()
    .SetupAspNetCoreAndDatabase(options =>
    {
                    //Migrate individual account database
                    options.RegisterServiceToRunInJob<StartupServiceMigrateAnyDbContext<ApplicationDbContext>>();
                    //Add demo users to the database (if no individual account exist)
                    options.RegisterServiceToRunInJob<StartupServicesIndividualAccountsAddDemoUsers>();

                    //Migrate the application part of the database
                    options.RegisterServiceToRunInJob<StartupServiceMigrateAnyDbContext<ShardingSingleDbContext>>();
                    //This seeds the invoice database (if empty)
                    options.RegisterServiceToRunInJob<StartupServiceSeedShardingDbContext>();
    });

//This is used to set a tenant as "Down",
builder.Services.AddDistributedFileStoreCache(options =>
{
    options.WhichVersion = FileStoreCacheVersions.Class;
    //I override the the default first part of the FileStore cache file because there are many example apps in this repo
    options.FirstPartOfCacheFileName = "Example6CacheFileStore";
}, builder.Environment);

//manually add services from the AuthPermissions.SupportCode project
builder.Services.AddTransient<IAccessDatabaseInformation, AccessDatabaseInformation>();

//Have to manually register this as its in the ExampleCommonCode project
builder.Services.AddSingleton<IGlobalChangeTimeService, GlobalChangeTimeService>();
builder.Services.AddTransient<ISetRemoveStatusService, SetRemoveStatusService>();

builder.Services.RegisterExample6Invoices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
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
app.AddDownForMaintenance(TenantTypes.SingleLevel | TenantTypes.AddSharding);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
