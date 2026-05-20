namespace WebWritingTool.Web.Configuration;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Accounts;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Web.Authorization;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services
            .AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = environment.IsDevelopment()
                ? "WebWritingTool.Auth"
                : "__Host-WebWritingTool.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/forbidden";
        });

        services.Configure<AdminSeedOptions>(
            configuration.GetSection(AdminSeedOptions.SectionName));

        services.AddScoped<IIdentityDataSeeder, IdentityDataSeeder>();
        services.AddScoped<IAccountWithdrawalService, AccountWithdrawalService>();

        return services;
    }

    public static IServiceCollection AddWebAuthorization(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApplicationPolicies.RequireAdmin, policy =>
                policy.RequireRole(ApplicationRoles.Admin));

            options.AddPolicy(ApplicationPolicies.AdminManageUser, policy =>
                policy.RequireRole(ApplicationRoles.Admin));

            options.AddPolicy(ApplicationPolicies.AdminDeleteUser, policy =>
                policy.RequireRole(ApplicationRoles.Admin));

            options.AddPolicy(ApplicationPolicies.SelfWithdrawAccount, policy =>
                policy.RequireAuthenticatedUser());

            AddOwnerPolicy(options, ApplicationPolicies.OwnArticle);
            AddOwnerPolicy(options, ApplicationPolicies.OwnWordpressSite);
            AddOwnerPolicy(options, ApplicationPolicies.OwnNotificationSetting);
            AddOwnerPolicy(options, ApplicationPolicies.OwnJob);
        });

        services.AddSingleton<IAuthorizationHandler, ResourceOwnerAuthorizationHandler>();

        return services;
    }

    private static void AddOwnerPolicy(AuthorizationOptions options, string policyName)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.Requirements.Add(new ResourceOwnerRequirement());
        });
    }
}
