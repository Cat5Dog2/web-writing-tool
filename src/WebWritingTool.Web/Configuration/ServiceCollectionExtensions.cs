namespace WebWritingTool.Web.Configuration;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using WebWritingTool.Application.Accounts;
using WebWritingTool.Application.Admin;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Jobs;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Infrastructure.Accounts;
using WebWritingTool.Infrastructure.Admin;
using WebWritingTool.Infrastructure.Articles;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.BackgroundJobs.Handlers;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Generation;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Infrastructure.Jobs;
using WebWritingTool.Infrastructure.Notifications;
using WebWritingTool.Infrastructure.Search;
using WebWritingTool.Infrastructure.Security;
using WebWritingTool.Infrastructure.Wordpress;
using WebWritingTool.Web.Authorization;
using WebWritingTool.Web.BackgroundJobs;
using WebWritingTool.Web.HealthChecks;
using WebWritingTool.Web.Security;

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
        services.Configure<BackgroundJobOptions>(
            configuration.GetSection(BackgroundJobOptions.SectionName));
        services
            .AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "Gemini model is required.")
            .Validate(
                options => string.Equals(options.Region, GeminiOptions.DefaultRegion, StringComparison.OrdinalIgnoreCase),
                "Gemini region must be Japan for MVP.")
            .Validate(options => options.TimeoutSeconds > 0, "Gemini timeout must be greater than zero.");
        services
            .AddOptions<SearchProviderOptions>()
            .Bind(configuration.GetSection(SearchProviderOptions.SectionName))
            .Validate(options => options.Tavily.TimeoutSeconds > 0, "Tavily timeout must be greater than zero.")
            .Validate(options => options.X.TimeoutSeconds > 0, "X timeout must be greater than zero.")
            .Validate(options => options.X.DefaultMaxResults is >= 10 and <= 100, "X default max results must be between 10 and 100.")
            .Validate(options => options.X.BulkMaxResults is >= 100 and <= 500, "X bulk max results must be between 100 and 500.")
            .Validate(options => options.X.MonthlySafetyLimitPosts > 0, "X monthly safety limit must be greater than zero.");
        services
            .AddOptions<SearchCachePolicyOptions>()
            .Bind(configuration.GetSection(SearchCachePolicyOptions.SectionName))
            .Validate(
                options => SearchCachePolicies.Allowed.Contains(SearchCachePolicyResolver.NormalizePolicy(options.Policy)),
                "Search cache policy is invalid.");
        services
            .AddOptions<WordpressOptions>()
            .Bind(configuration.GetSection(WordpressOptions.SectionName))
            .Validate(options => options.TimeoutSeconds > 0, "WordPress timeout must be greater than zero.")
            .Validate(
                options => options.AllowedSchemes.Length == 1
                    && string.Equals(options.AllowedSchemes[0], Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase),
                "WordPress allowed schemes must be https only.");
        services
            .AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(
                options => string.Equals(options.Provider, NotificationProviders.Discord, StringComparison.Ordinal),
                "Notification provider must be Discord for MVP.")
            .Validate(options => options.TimeoutSeconds > 0, "Notification timeout must be greater than zero.");
        services
            .AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName))
            .Validate(
                options => !environment.IsProduction() || !string.IsNullOrWhiteSpace(options.DataProtectionKeysPath),
                "Security:DataProtectionKeysPath is required in Production.")
            .ValidateOnStart();

        ConfigureForwardedHeaders(services, configuration, environment);
        ConfigureDataProtection(services, configuration, environment);
        services.AddOperationalHealthChecks();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = CsrfEndpointFilter.HeaderName;
        });
        services.AddSecurityRateLimiting(environment);
        services.AddScoped<IIdentityDataSeeder, IdentityDataSeeder>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<ISecretMasker, SecretMasker>();
        services.AddSingleton<ISecurityRateLimiter, InMemorySecurityRateLimiter>();
        services.AddSingleton<IUrlSafetyValidator, DnsUrlSafetyValidator>();
        services.AddScoped<UserOwnedDataDeletionService>();
        services.AddScoped<IAccountWithdrawalService, AccountWithdrawalService>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<ArticleService>();
        services.AddScoped<IArticleCommandService>(provider => provider.GetRequiredService<ArticleService>());
        services.AddScoped<IArticleQueryService>(provider => provider.GetRequiredService<ArticleService>());
        services.AddScoped<IArticleHeadingService, ArticleHeadingService>();
        services.AddScoped<IArticleContentService, ArticleContentService>();
        services.AddScoped<WordpressSiteService>();
        services.AddScoped<IWordpressSiteCommandService>(provider => provider.GetRequiredService<WordpressSiteService>());
        services.AddScoped<IWordpressSiteQueryService>(provider => provider.GetRequiredService<WordpressSiteService>());
        services.AddScoped<WordpressPostService>();
        services.AddScoped<IWordpressPostCommandService>(provider => provider.GetRequiredService<WordpressPostService>());
        services.AddScoped<IWordpressPostQueryService>(provider => provider.GetRequiredService<WordpressPostService>());
        services.AddScoped<NotificationSettingService>();
        services.AddScoped<INotificationSettingService>(provider => provider.GetRequiredService<NotificationSettingService>());
        services.AddScoped<INotificationTestService>(provider => provider.GetRequiredService<NotificationSettingService>());
        services.AddScoped<INotificationJobService, NotificationJobService>();
        services.AddScoped<IXPostRehydrationService, XPostRehydrationService>();
        services.AddScoped<SearchCacheCleanupService>();
        services.AddSingleton<BackgroundWorkerHealthState>();
        services.AddSingleton<IContentRenderingService, ContentRenderingService>();
        services.AddSingleton<TitleGenerationPromptBuilder>();
        services.AddSingleton<OutlineGenerationPromptBuilder>();
        services.AddSingleton<BodyGenerationPromptBuilder>();
        services.AddSingleton<RewritePromptBuilder>();
        services.AddSingleton(provider =>
            new SearchCachePolicyResolver(
                provider.GetRequiredService<IOptions<SearchCachePolicyOptions>>().Value));
        services.AddSingleton(TopicRiskKeywordDictionary.Default);
        services.AddSingleton<ITopicRiskClassifier, TopicRiskClassifier>();
        services.AddHttpClient<IAiTextGenerationClient, GeminiTextGenerationClient>((provider, client) =>
        {
            var geminiOptions = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;
            client.BaseAddress = geminiOptions.EndpointBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(geminiOptions.TimeoutSeconds);
        });
        services.AddHttpClient<IWebSearchClient, TavilyWebSearchClient>((provider, client) =>
        {
            var searchOptions = provider.GetRequiredService<IOptions<SearchProviderOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(searchOptions.Tavily.TimeoutSeconds);
        });
        services.AddHttpClient<IXFullArchiveSearchClient, XFullArchiveSearchClient>((provider, client) =>
        {
            var searchOptions = provider.GetRequiredService<IOptions<SearchProviderOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(searchOptions.X.TimeoutSeconds);
        });
        if (environment.IsEnvironment("Test"))
        {
            services.AddSingleton<IWordpressClient, TestWordpressClient>();
        }
        else
        {
            services.AddHttpClient<IWordpressClient, WordpressClient>((provider, client) =>
            {
                var wordpressOptions = provider.GetRequiredService<IOptions<WordpressOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(wordpressOptions.TimeoutSeconds);
            });
        }
        if (environment.IsEnvironment("Test"))
        {
            services.AddSingleton<IDiscordNotificationClient, TestDiscordNotificationClient>();
        }
        else
        {
            services.AddHttpClient<IDiscordNotificationClient, DiscordNotificationClient>((provider, client) =>
            {
                var notificationOptions = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(notificationOptions.TimeoutSeconds);
            });
        }
        services.AddSingleton<JobRetryPolicy>();
        services.AddScoped<JobLeaseService>();
        services.AddScoped<JobDispatcher>();
        services.AddScoped<JobService>();
        services.AddScoped<IJobCommandService>(provider => provider.GetRequiredService<JobService>());
        services.AddScoped<IJobQueryService>(provider => provider.GetRequiredService<JobService>());
        services.AddScoped<IJobHandler, TitleGenerationJobHandler>();
        services.AddScoped<IJobHandler, OutlineGenerationJobHandler>();
        services.AddScoped<IJobHandler, BodyGenerationJobHandler>();
        services.AddScoped<IJobHandler, RewriteJobHandler>();
        services.AddScoped<IJobHandler, WebSearchJobHandler>();
        services.AddScoped<IJobHandler, XFullArchiveSearchJobHandler>();
        services.AddScoped<IJobHandler, WordpressPostJobHandler>();
        services.AddScoped<IJobHandler, NotificationJobHandler>();
        services.AddHostedService<ArticleJobWorker>();
        services.AddHostedService<SearchCacheCleanupWorker>();

        return services;
    }

    private static IServiceCollection AddOperationalHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(
                "postgres",
                tags: ["ready"])
            .AddCheck<BackgroundWorkerHealthCheck>(
                "background_workers",
                tags: ["ready"])
            .AddCheck<ExternalDependencyConfigurationHealthCheck>(
                "external_dependency_configuration",
                tags: ["deps"]);

        return services;
    }

    private static void ConfigureForwardedHeaders(
        IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var securityOptions = GetSecurityOptions(configuration);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;

            if (securityOptions.ShouldUseForwardedHeaders(environment))
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }

            foreach (var host in securityOptions.AllowedForwardedHosts.Where(host => !string.IsNullOrWhiteSpace(host)))
            {
                options.AllowedHosts.Add(host.Trim());
            }
        });
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var securityOptions = GetSecurityOptions(configuration);
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(securityOptions.DataProtectionKeysPath))
        {
            throw new InvalidOperationException("Security:DataProtectionKeysPath is required in Production.");
        }

        var dataProtectionBuilder = services
            .AddDataProtection()
            .SetApplicationName(SecurityOptions.DataProtectionApplicationName);

        if (!string.IsNullOrWhiteSpace(securityOptions.DataProtectionKeysPath))
        {
            var keyDirectory = Directory.CreateDirectory(securityOptions.DataProtectionKeysPath);
            dataProtectionBuilder.PersistKeysToFileSystem(keyDirectory);
        }
    }

    private static SecurityOptions GetSecurityOptions(IConfiguration configuration)
    {
        return configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
            ?? new SecurityOptions();
    }

    private static IServiceCollection AddSecurityRateLimiting(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        var isTest = environment.IsEnvironment("Test");

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            AddFixedWindowPolicy(
                options,
                SecurityRateLimitPolicyNames.Login,
                permitLimit: isTest ? 100 : 10,
                TimeSpan.FromMinutes(1),
                useAuthenticatedUser: false);
            AddFixedWindowPolicy(
                options,
                SecurityRateLimitPolicyNames.BulkArticleRegistration,
                permitLimit: isTest ? 100 : 3,
                TimeSpan.FromMinutes(1));
            AddFixedWindowPolicy(
                options,
                SecurityRateLimitPolicyNames.JobRegistration,
                permitLimit: isTest ? 100 : 30,
                TimeSpan.FromMinutes(1));
            AddFixedWindowPolicy(
                options,
                SecurityRateLimitPolicyNames.NotificationTest,
                permitLimit: isTest ? 100 : 5,
                TimeSpan.FromMinutes(10));
            AddFixedWindowPolicy(
                options,
                SecurityRateLimitPolicyNames.WordpressPost,
                permitLimit: isTest ? 100 : 10,
                TimeSpan.FromMinutes(1));
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        int permitLimit,
        TimeSpan window,
        bool useAuthenticatedUser = true)
    {
        options.AddPolicy(policyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                GetRateLimitPartitionKey(httpContext, policyName, useAuthenticatedUser),
                _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = permitLimit,
                    QueueLimit = 0,
                    Window = window
                }));
    }

    private static string GetRateLimitPartitionKey(
        HttpContext httpContext,
        string policyName,
        bool useAuthenticatedUser)
    {
        var userId = useAuthenticatedUser
            ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        var client = string.IsNullOrWhiteSpace(userId)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : userId;

        return $"{policyName}:{client}";
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
