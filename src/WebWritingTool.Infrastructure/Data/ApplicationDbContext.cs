using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WebWritingTool.Domain.Ai;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Audit;
using WebWritingTool.Domain.Common;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Notifications;
using WebWritingTool.Domain.Search;
using WebWritingTool.Domain.Usage;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Identity;

namespace WebWritingTool.Infrastructure.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole, string>(options)
{
    private const string TimestampWithTimeZone = "timestamp with time zone";
    private const string Jsonb = "jsonb";

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<ArticleHeading> ArticleHeadings => Set<ArticleHeading>();

    public DbSet<ArticleGenerationJob> ArticleGenerationJobs => Set<ArticleGenerationJob>();

    public DbSet<AiGenerationLog> AiGenerationLogs => Set<AiGenerationLog>();

    public DbSet<UsageLedger> UsageLedgers => Set<UsageLedger>();

    public DbSet<SearchResult> SearchResults => Set<SearchResult>();

    public DbSet<XSearchPost> XSearchPosts => Set<XSearchPost>();

    public DbSet<WordpressSite> WordpressSites => Set<WordpressSite>();

    public DbSet<WordpressPost> WordpressPosts => Set<WordpressPost>();

    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();

    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    public DbSet<AiModelSetting> AiModelSettings => Set<AiModelSetting>();

    public DbSet<UserUsageLimit> UserUsageLimits => Set<UserUsageLimit>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public override int SaveChanges()
    {
        StampEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureApplicationUser(builder.Entity<ApplicationUser>());
        ConfigureArticle(builder.Entity<Article>());
        ConfigureArticleHeading(builder.Entity<ArticleHeading>());
        ConfigureArticleGenerationJob(builder.Entity<ArticleGenerationJob>());
        ConfigureAiGenerationLog(builder.Entity<AiGenerationLog>());
        ConfigureUsageLedger(builder.Entity<UsageLedger>());
        ConfigureSearchResult(builder.Entity<SearchResult>());
        ConfigureXSearchPost(builder.Entity<XSearchPost>());
        ConfigureWordpressSite(builder.Entity<WordpressSite>());
        ConfigureWordpressPost(builder.Entity<WordpressPost>());
        ConfigureNotificationSetting(builder.Entity<NotificationSetting>());
        ConfigureNotificationLog(builder.Entity<NotificationLog>());
        ConfigureAiModelSetting(builder.Entity<AiModelSetting>());
        ConfigureUserUsageLimit(builder.Entity<UserUsageLimit>());
        ConfigureAuditLog(builder.Entity<AuditLog>());
    }

    private static void ConfigureApplicationUser(EntityTypeBuilder<ApplicationUser> entity)
    {
        entity.Property(user => user.DisplayName)
            .HasMaxLength(100);

        entity.Property(user => user.IsEnabled)
            .HasDefaultValue(true);

        entity.Property(user => user.LastLoginAt)
            .HasColumnType(TimestampWithTimeZone);

        entity.Property(user => user.CreatedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();

        entity.Property(user => user.UpdatedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
    }

    private static void ConfigureArticle(EntityTypeBuilder<Article> entity)
    {
        entity.ToTable("Articles");
        entity.HasKey(article => article.Id);

        entity.Property(article => article.Keyword)
            .HasMaxLength(200)
            .IsRequired();
        entity.Property(article => article.Title)
            .HasMaxLength(250);
        entity.Property(article => article.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(article => article.Tone)
            .HasMaxLength(40);
        entity.Property(article => article.Tags)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]")
            .IsRequired();
        entity.Property(article => article.LearningType)
            .HasMaxLength(40);
        entity.Property(article => article.MetaDescription)
            .HasMaxLength(320);
        entity.Property(article => article.GenerationModel)
            .HasMaxLength(80);
        entity.Property(article => article.OutlineMethod)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(article => article.SearchMode)
            .HasDefaultValue(false);
        entity.Property(article => article.IsDomesticOnly)
            .HasDefaultValue(true);
        entity.Property(article => article.NotificationMode)
            .HasMaxLength(40)
            .HasDefaultValue("None")
            .IsRequired();
        entity.Property(article => article.StrictMode)
            .HasDefaultValue(false);
        entity.Property(article => article.TopicRisk)
            .HasMaxLength(40);
        entity.Property(article => article.HumanReviewRequired)
            .HasDefaultValue(false);
        entity.Property(article => article.HumanReviewedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(article => article.WritingProfileSnapshotJson)
            .HasColumnType(Jsonb);
        entity.Property(article => article.AutoPostToWordpress)
            .HasDefaultValue(false);
        entity.Property(article => article.AutoPostQueuedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(article => article.CompletedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(article => article.PostedAt)
            .HasColumnType(TimestampWithTimeZone);

        ConfigureAuditableEntity(entity);
        ConfigureSoftDelete(entity);
        ConfigureRowVersion(entity);
        entity.HasQueryFilter(article => article.DeletedAt == null);

        entity.HasIndex(article => new { article.UserId, article.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Articles_UserId_CreatedAt");
        entity.HasIndex(article => new { article.UserId, article.Status })
            .HasDatabaseName("IX_Articles_UserId_Status");
        entity.HasIndex(article => new { article.UserId, article.Title })
            .HasDatabaseName("IX_Articles_UserId_Title");
        entity.HasIndex(article => article.DeletedAt)
            .HasDatabaseName("IX_Articles_DeletedAt");
        entity.HasIndex(article => article.Tags)
            .HasMethod("gin")
            .HasDatabaseName("IX_Articles_Tags_Gin");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(article => article.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(article => article.HumanReviewedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<WordpressSite>()
            .WithMany()
            .HasForeignKey(article => article.WritingProfileWordpressSiteId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<WordpressSite>()
            .WithMany()
            .HasForeignKey(article => article.AutoPostWordpressSiteId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureArticleHeading(EntityTypeBuilder<ArticleHeading> entity)
    {
        entity.ToTable("ArticleHeadings", table =>
        {
            table.HasCheckConstraint("CK_ArticleHeadings_Level", "\"Level\" IN (2, 3)");
            table.HasCheckConstraint(
                "CK_ArticleHeadings_Level_Parent",
                "((\"Level\" = 2 AND \"ParentId\" IS NULL) OR (\"Level\" = 3 AND \"ParentId\" IS NOT NULL))");
        });
        entity.HasKey(heading => heading.Id);
        entity.HasAlternateKey(heading => new { heading.ArticleId, heading.Id })
            .HasName("AK_ArticleHeadings_ArticleId_Id");

        entity.Property(heading => heading.Title)
            .HasMaxLength(250)
            .IsRequired();
        entity.Property(heading => heading.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(heading => heading.UseWebSearch)
            .HasDefaultValue(false);
        entity.Property(heading => heading.SearchQuery)
            .HasMaxLength(300);

        ConfigureAuditableEntity(entity);
        ConfigureSoftDelete(entity);
        ConfigureRowVersion(entity);
        entity.HasQueryFilter(heading => heading.DeletedAt == null);

        entity.HasIndex(heading => new { heading.ArticleId, heading.DisplayOrder })
            .HasDatabaseName("IX_ArticleHeadings_ArticleId_DisplayOrder");
        entity.HasIndex(heading => new { heading.ArticleId, heading.ParentId })
            .HasDatabaseName("IX_ArticleHeadings_ArticleId_ParentId");
        entity.HasIndex(heading => heading.Status)
            .HasDatabaseName("IX_ArticleHeadings_Status");

        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(heading => heading.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ArticleHeading>()
            .WithMany()
            .HasForeignKey(heading => new { heading.ArticleId, heading.ParentId })
            .HasPrincipalKey(heading => new { heading.ArticleId, heading.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureArticleGenerationJob(EntityTypeBuilder<ArticleGenerationJob> entity)
    {
        entity.ToTable("ArticleGenerationJobs", table =>
        {
            table.HasCheckConstraint("CK_ArticleGenerationJobs_Progress", "\"Progress\" BETWEEN 0 AND 100");
        });
        entity.HasKey(job => job.Id);

        entity.Property(job => job.JobType)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(job => job.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(job => job.Priority)
            .HasDefaultValue(0);
        entity.Property(job => job.Progress)
            .HasDefaultValue(0);
        entity.Property(job => job.PayloadJson)
            .HasColumnType(Jsonb)
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        entity.Property(job => job.ResultJson)
            .HasColumnType(Jsonb);
        entity.Property(job => job.AttemptCount)
            .HasDefaultValue(0);
        entity.Property(job => job.MaxAttempts)
            .HasDefaultValue(3);
        entity.Property(job => job.NextRunAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(job => job.LockedBy)
            .HasMaxLength(100);
        entity.Property(job => job.LockedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(job => job.ErrorCode)
            .HasMaxLength(80);
        entity.Property(job => job.QueuedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
        entity.Property(job => job.StartedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(job => job.FinishedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(job => job.CanceledAt)
            .HasColumnType(TimestampWithTimeZone);

        entity.HasIndex(job => new { job.Status, job.Priority, job.QueuedAt })
            .IsDescending(false, true, false)
            .HasDatabaseName("IX_ArticleGenerationJobs_Status_Priority_QueuedAt");
        entity.HasIndex(job => new { job.Status, job.NextRunAt })
            .HasDatabaseName("IX_ArticleGenerationJobs_Status_NextRunAt");
        entity.HasIndex(job => new { job.UserId, job.QueuedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ArticleGenerationJobs_UserId_QueuedAt");
        entity.HasIndex(job => new { job.ArticleId, job.JobType })
            .HasDatabaseName("IX_ArticleGenerationJobs_ArticleId_JobType");
        entity.HasIndex(job => job.HeadingId)
            .HasDatabaseName("IX_ArticleGenerationJobs_HeadingId");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(job => job.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(job => job.ArticleId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<ArticleHeading>()
            .WithMany()
            .HasForeignKey(job => job.HeadingId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureAiGenerationLog(EntityTypeBuilder<AiGenerationLog> entity)
    {
        entity.ToTable("AiGenerationLogs");
        entity.HasKey(log => log.Id);

        entity.Property(log => log.Provider)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(log => log.Model)
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(log => log.Operation)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(log => log.PromptHash)
            .HasMaxLength(128);
        entity.Property(log => log.ErrorCode)
            .HasMaxLength(80);
        ConfigureCreatedAtEntity(entity);

        entity.HasIndex(log => new { log.UserId, log.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AiGenerationLogs_UserId_CreatedAt");
        entity.HasIndex(log => log.ArticleId)
            .HasDatabaseName("IX_AiGenerationLogs_ArticleId");
        entity.HasIndex(log => log.JobId)
            .HasDatabaseName("IX_AiGenerationLogs_JobId");
        entity.HasIndex(log => new { log.Model, log.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AiGenerationLogs_Model_CreatedAt");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(log => log.ArticleId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<ArticleGenerationJob>()
            .WithMany()
            .HasForeignKey(log => log.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureUsageLedger(EntityTypeBuilder<UsageLedger> entity)
    {
        entity.ToTable("UsageLedgers");
        entity.HasKey(ledger => ledger.Id);

        entity.Property(ledger => ledger.Provider)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(ledger => ledger.Model)
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(ledger => ledger.Operation)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(ledger => ledger.OccurredAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();

        entity.HasIndex(ledger => new { ledger.UserId, ledger.OccurredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_UsageLedgers_UserId_OccurredAt");
        entity.HasIndex(ledger => ledger.JobId)
            .HasDatabaseName("IX_UsageLedgers_JobId");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(ledger => ledger.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(ledger => ledger.ArticleId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<ArticleGenerationJob>()
            .WithMany()
            .HasForeignKey(ledger => ledger.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureSearchResult(EntityTypeBuilder<SearchResult> entity)
    {
        entity.ToTable("SearchResults");
        entity.HasKey(result => result.Id);

        entity.Property(result => result.Query)
            .HasMaxLength(300)
            .IsRequired();
        entity.Property(result => result.Title)
            .HasMaxLength(500);
        entity.Property(result => result.Url)
            .IsRequired();
        entity.Property(result => result.Provider)
            .HasMaxLength(40);
        entity.Property(result => result.QueryHash)
            .HasMaxLength(128);
        entity.Property(result => result.CacheExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(result => result.RawJsonExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(result => result.ContentExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(result => result.MetadataExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(result => result.FetchedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();

        entity.HasIndex(result => new { result.ArticleId, result.FetchedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SearchResults_ArticleId_FetchedAt");
        entity.HasIndex(result => result.HeadingId)
            .HasDatabaseName("IX_SearchResults_HeadingId");
        entity.HasIndex(result => result.Query)
            .HasDatabaseName("IX_SearchResults_Query");
        entity.HasIndex(result => new { result.QueryHash, result.CacheExpiresAt })
            .HasDatabaseName("IX_SearchResults_QueryHash_CacheExpiresAt");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(result => result.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(result => result.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ArticleHeading>()
            .WithMany()
            .HasForeignKey(result => result.HeadingId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureXSearchPost(EntityTypeBuilder<XSearchPost> entity)
    {
        entity.ToTable("XSearchPosts");
        entity.HasKey(post => post.Id);

        entity.Property(post => post.Query)
            .HasMaxLength(300)
            .IsRequired();
        entity.Property(post => post.QueryHash)
            .HasMaxLength(128)
            .IsRequired();
        entity.Property(post => post.PostId)
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(post => post.AuthorId)
            .HasMaxLength(80);
        entity.Property(post => post.Language)
            .HasMaxLength(20);
        entity.Property(post => post.PostedAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(post => post.FetchedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
        entity.Property(post => post.CacheExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(post => post.ContentExpiresAt)
            .HasColumnType(TimestampWithTimeZone);
        entity.Property(post => post.MetadataExpiresAt)
            .HasColumnType(TimestampWithTimeZone);

        entity.HasIndex(post => post.PostId)
            .IsUnique()
            .HasDatabaseName("UX_XSearchPosts_PostId");
        entity.HasIndex(post => new { post.ArticleId, post.FetchedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_XSearchPosts_ArticleId_FetchedAt");
        entity.HasIndex(post => post.HeadingId)
            .HasDatabaseName("IX_XSearchPosts_HeadingId");
        entity.HasIndex(post => new { post.QueryHash, post.CacheExpiresAt })
            .HasDatabaseName("IX_XSearchPosts_QueryHash_CacheExpiresAt");
        entity.HasIndex(post => post.PostedAt)
            .HasDatabaseName("IX_XSearchPosts_PostedAt");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(post => post.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(post => post.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ArticleHeading>()
            .WithMany()
            .HasForeignKey(post => post.HeadingId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureWordpressSite(EntityTypeBuilder<WordpressSite> entity)
    {
        entity.ToTable("WordpressSites");
        entity.HasKey(site => site.Id);

        entity.Property(site => site.SiteName)
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(site => site.BaseUrl)
            .IsRequired();
        entity.Property(site => site.LoginId)
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(site => site.EncryptedApplicationPassword)
            .IsRequired();
        entity.Property(site => site.DefaultCategoryName)
            .HasMaxLength(200);
        entity.Property(site => site.LastConnectedAt)
            .HasColumnType(TimestampWithTimeZone);

        ConfigureAuditableEntity(entity);
        ConfigureSoftDelete(entity);
        ConfigureRowVersion(entity);
        entity.HasQueryFilter(site => site.DeletedAt == null);

        entity.HasIndex(site => new { site.UserId, site.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_WordpressSites_UserId_CreatedAt");
        entity.HasIndex(site => new { site.UserId, site.DeletedAt })
            .HasDatabaseName("IX_WordpressSites_UserId_DeletedAt");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(site => site.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWordpressPost(EntityTypeBuilder<WordpressPost> entity)
    {
        entity.ToTable("WordpressPosts");
        entity.HasKey(post => post.Id);

        entity.Property(post => post.Title)
            .HasMaxLength(250)
            .IsRequired();
        entity.Property(post => post.RequestedStatus)
            .HasMaxLength(40)
            .HasDefaultValue("Draft")
            .IsRequired();
        entity.Property(post => post.Status)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(post => post.ErrorCode)
            .HasMaxLength(80);
        entity.Property(post => post.PostedAt)
            .HasColumnType(TimestampWithTimeZone);
        ConfigureCreatedAtEntity(entity);

        entity.HasIndex(post => new { post.ArticleId, post.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_WordpressPosts_ArticleId_CreatedAt");
        entity.HasIndex(post => new { post.WordpressSiteId, post.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_WordpressPosts_WordpressSiteId_CreatedAt");
        entity.HasIndex(post => new { post.UserId, post.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_WordpressPosts_UserId_CreatedAt");
        entity.HasIndex(post => post.JobId)
            .HasDatabaseName("IX_WordpressPosts_JobId");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(post => post.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(post => post.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<WordpressSite>()
            .WithMany()
            .HasForeignKey(post => post.WordpressSiteId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ArticleGenerationJob>()
            .WithMany()
            .HasForeignKey(post => post.JobId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureNotificationSetting(EntityTypeBuilder<NotificationSetting> entity)
    {
        entity.ToTable("NotificationSettings");
        entity.HasKey(setting => setting.Id);

        entity.Property(setting => setting.Provider)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(setting => setting.DestinationMasked)
            .HasMaxLength(500)
            .IsRequired();
        entity.Property(setting => setting.EncryptedWebhookUrl)
            .IsRequired();
        entity.Property(setting => setting.Enabled)
            .HasDefaultValue(true);

        ConfigureAuditableEntity(entity);
        ConfigureSoftDelete(entity);
        ConfigureRowVersion(entity);
        entity.HasQueryFilter(setting => setting.DeletedAt == null);

        entity.HasIndex(setting => new { setting.UserId, setting.Provider })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("UX_NotificationSettings_UserId_Provider_Active");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(setting => setting.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureNotificationLog(EntityTypeBuilder<NotificationLog> entity)
    {
        entity.ToTable("NotificationLogs");
        entity.HasKey(log => log.Id);

        entity.Property(log => log.Provider)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(log => log.DestinationMasked)
            .HasMaxLength(500)
            .IsRequired();
        entity.Property(log => log.EventType)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(log => log.Status)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(log => log.ErrorCode)
            .HasMaxLength(80);
        ConfigureCreatedAtEntity(entity);

        entity.HasIndex(log => new { log.UserId, log.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_NotificationLogs_UserId_CreatedAt");
        entity.HasIndex(log => log.ArticleId)
            .HasDatabaseName("IX_NotificationLogs_ArticleId");
        entity.HasIndex(log => log.JobId)
            .HasDatabaseName("IX_NotificationLogs_JobId");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Article>()
            .WithMany()
            .HasForeignKey(log => log.ArticleId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<ArticleGenerationJob>()
            .WithMany()
            .HasForeignKey(log => log.JobId)
            .OnDelete(DeleteBehavior.SetNull);
        entity.HasOne<NotificationSetting>()
            .WithMany()
            .HasForeignKey(log => log.NotificationSettingId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureAiModelSetting(EntityTypeBuilder<AiModelSetting> entity)
    {
        entity.ToTable("AiModelSettings");
        entity.HasKey(setting => setting.Id);

        entity.Property(setting => setting.Provider)
            .HasMaxLength(40)
            .IsRequired();
        entity.Property(setting => setting.Model)
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(setting => setting.DisplayName)
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(setting => setting.Region)
            .HasMaxLength(80)
            .HasDefaultValue("Japan")
            .IsRequired();
        entity.Property(setting => setting.Enabled)
            .HasDefaultValue(true);
        entity.Property(setting => setting.SortOrder)
            .HasDefaultValue(0);
        ConfigureAuditableEntity(entity);

        entity.HasIndex(setting => new { setting.Provider, setting.Model })
            .IsUnique()
            .HasDatabaseName("UX_AiModelSettings_Provider_Model");
        entity.HasIndex(setting => new { setting.Enabled, setting.SortOrder })
            .HasDatabaseName("IX_AiModelSettings_Enabled_SortOrder");
    }

    private static void ConfigureUserUsageLimit(EntityTypeBuilder<UserUsageLimit> entity)
    {
        entity.ToTable("UserUsageLimits");
        entity.HasKey(limit => limit.Id);

        entity.Property(limit => limit.DefaultStrictMode)
            .HasDefaultValue(false);
        entity.Property(limit => limit.MaxSearchCachePolicy)
            .HasMaxLength(40);

        ConfigureAuditableEntity(entity);
        ConfigureRowVersion(entity);

        entity.HasIndex(limit => limit.UserId)
            .IsUnique()
            .HasDatabaseName("UX_UserUsageLimits_UserId");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(limit => limit.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAuditLog(EntityTypeBuilder<AuditLog> entity)
    {
        entity.ToTable("AuditLogs");
        entity.HasKey(log => log.Id);

        entity.Property(log => log.Action)
            .HasMaxLength(80)
            .IsRequired();
        entity.Property(log => log.EntityType)
            .HasMaxLength(80);
        entity.Property(log => log.EntityId)
            .HasMaxLength(100);
        entity.Property(log => log.MetadataJson)
            .HasColumnType(Jsonb);
        entity.Property(log => log.IpAddress)
            .HasMaxLength(80);
        ConfigureCreatedAtEntity(entity);

        entity.HasIndex(log => new { log.UserId, log.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditLogs_UserId_CreatedAt");
        entity.HasIndex(log => new { log.EntityType, log.EntityId })
            .HasDatabaseName("IX_AuditLogs_EntityType_EntityId");
        entity.HasIndex(log => new { log.Action, log.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditLogs_Action_CreatedAt");

        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAuditableEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IAuditableEntity
    {
        entity.Property(item => item.CreatedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
        entity.Property(item => item.UpdatedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
    }

    private static void ConfigureCreatedAtEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, ICreatedAtEntity
    {
        entity.Property(item => item.CreatedAt)
            .HasColumnType(TimestampWithTimeZone)
            .IsRequired();
    }

    private static void ConfigureSoftDelete<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, ISoftDeletableEntity
    {
        entity.Property(item => item.DeletedAt)
            .HasColumnType(TimestampWithTimeZone);
    }

    private static void ConfigureRowVersion<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : class, IRowVersionEntity
    {
        entity.Property(item => item.RowVersion)
            .HasColumnType("bytea")
            .IsRequired()
            .IsConcurrencyToken();
    }

    private void StampEntities()
    {
        var now = DateTimeOffset.UtcNow;

        StampApplicationUsers(now);
        StampCreatedAtEntities(now);
        StampAuditableEntities(now);
        StampRowVersionEntities();
    }

    private void StampApplicationUsers(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(user => user.CreatedAt).IsModified = false;
            }
        }
    }

    private void StampCreatedAtEntities(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<ICreatedAtEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(entity => entity.CreatedAt).IsModified = false;
            }
        }
    }

    private void StampAuditableEntities(DateTimeOffset now)
    {
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(entity => entity.CreatedAt).IsModified = false;
            }
        }
    }

    private void StampRowVersionEntities()
    {
        foreach (var entry in ChangeTracker.Entries<IRowVersionEntity>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = RandomNumberGenerator.GetBytes(16);
            }
        }
    }
}
