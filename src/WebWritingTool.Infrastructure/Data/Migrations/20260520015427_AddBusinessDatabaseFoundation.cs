using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWritingTool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessDatabaseFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "Japan"),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MaxInputChars = table.Column<int>(type: "integer", nullable: true),
                    MaxOutputChars = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DestinationMasked = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EncryptedWebhookUrl = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserUsageLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    MonthlyLimitChars = table.Column<int>(type: "integer", nullable: false),
                    RemainingOutlineCount = table.Column<int>(type: "integer", nullable: false),
                    DefaultStrictMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MaxSearchCachePolicy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserUsageLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserUsageLimits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WordpressSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SiteName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "text", nullable: false),
                    LoginId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EncryptedApplicationPassword = table.Column<string>(type: "text", nullable: false),
                    DefaultCategoryId = table.Column<int>(type: "integer", nullable: true),
                    DefaultCategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SiteAdminProfile = table.Column<string>(type: "text", nullable: true),
                    WritingCharacter = table.Column<string>(type: "text", nullable: true),
                    ReaderPersona = table.Column<string>(type: "text", nullable: true),
                    LastConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordpressSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordpressSites_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Memo = table.Column<string>(type: "text", nullable: true),
                    SuggestedKeywords = table.Column<string>(type: "text", nullable: true),
                    RelatedKeywords = table.Column<string>(type: "text", nullable: true),
                    LearningType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LearningText = table.Column<string>(type: "text", nullable: true),
                    AdditionalPrompt = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    GenerationModel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    OutlineMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SearchMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsDomesticOnly = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotificationMode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "None"),
                    StrictMode = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TopicRisk = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    HumanReviewRequired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    HumanReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HumanReviewedByUserId = table.Column<string>(type: "text", nullable: true),
                    WritingProfileWordpressSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    WritingProfileSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    AutoPostToWordpress = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AutoPostWordpressSiteId = table.Column<Guid>(type: "uuid", nullable: true),
                    AutoPostWordpressCategoryId = table.Column<int>(type: "integer", nullable: true),
                    AutoPostQueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Articles_AspNetUsers_HumanReviewedByUserId",
                        column: x => x.HumanReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Articles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Articles_WordpressSites_AutoPostWordpressSiteId",
                        column: x => x.AutoPostWordpressSiteId,
                        principalTable: "WordpressSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Articles_WordpressSites_WritingProfileWordpressSiteId",
                        column: x => x.WritingProfileWordpressSiteId,
                        principalTable: "WordpressSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ArticleHeadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    TargetLength = table.Column<int>(type: "integer", nullable: true),
                    ActualLength = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    UseWebSearch = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SearchQuery = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleHeadings", x => x.Id);
                    table.UniqueConstraint("AK_ArticleHeadings_ArticleId_Id", x => new { x.ArticleId, x.Id });
                    table.CheckConstraint("CK_ArticleHeadings_Level", "\"Level\" IN (2, 3)");
                    table.CheckConstraint("CK_ArticleHeadings_Level_Parent", "((\"Level\" = 2 AND \"ParentId\" IS NULL) OR (\"Level\" = 3 AND \"ParentId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_ArticleHeadings_ArticleHeadings_ArticleId_ParentId",
                        columns: x => new { x.ArticleId, x.ParentId },
                        principalTable: "ArticleHeadings",
                        principalColumns: new[] { "ArticleId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArticleHeadings_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ArticleGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeadingId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CanceledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleGenerationJobs", x => x.Id);
                    table.CheckConstraint("CK_ArticleGenerationJobs_Progress", "\"Progress\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_ArticleGenerationJobs_ArticleHeadings_HeadingId",
                        column: x => x.HeadingId,
                        principalTable: "ArticleHeadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ArticleGenerationJobs_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ArticleGenerationJobs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SearchResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeadingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Query = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Snippet = table.Column<string>(type: "text", nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    QueryHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CacheExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawJsonExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MetadataExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchResults_ArticleHeadings_HeadingId",
                        column: x => x.HeadingId,
                        principalTable: "ArticleHeadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SearchResults_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SearchResults_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "XSearchPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    HeadingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Query = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    QueryHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PostId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AuthorId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CacheExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MetadataExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XSearchPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XSearchPosts_ArticleHeadings_HeadingId",
                        column: x => x.HeadingId,
                        principalTable: "ArticleHeadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_XSearchPosts_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_XSearchPosts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiGenerationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PromptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptChars = table.Column<int>(type: "integer", nullable: false),
                    OutputChars = table.Column<int>(type: "integer", nullable: false),
                    UsageChars = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiGenerationLogs_ArticleGenerationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ArticleGenerationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiGenerationLogs_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiGenerationLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationSettingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DestinationMasked = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_ArticleGenerationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ArticleGenerationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_NotificationSettings_NotificationSettingId",
                        column: x => x.NotificationSettingId,
                        principalTable: "NotificationSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UsageLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PromptChars = table.Column<int>(type: "integer", nullable: false),
                    OutputChars = table.Column<int>(type: "integer", nullable: false),
                    UsageChars = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsageLedgers_ArticleGenerationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ArticleGenerationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UsageLedgers_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UsageLedgers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WordpressPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    WordpressSiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: true),
                    PostUrl = table.Column<string>(type: "text", nullable: true),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    RequestedStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "Draft"),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordpressPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordpressPosts_ArticleGenerationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ArticleGenerationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WordpressPosts_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WordpressPosts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WordpressPosts_WordpressSites_WordpressSiteId",
                        column: x => x.WordpressSiteId,
                        principalTable: "WordpressSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationLogs_ArticleId",
                table: "AiGenerationLogs",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationLogs_JobId",
                table: "AiGenerationLogs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationLogs_Model_CreatedAt",
                table: "AiGenerationLogs",
                columns: new[] { "Model", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationLogs_UserId_CreatedAt",
                table: "AiGenerationLogs",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelSettings_Enabled_SortOrder",
                table: "AiModelSettings",
                columns: new[] { "Enabled", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_AiModelSettings_Provider_Model",
                table: "AiModelSettings",
                columns: new[] { "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_ArticleId_JobType",
                table: "ArticleGenerationJobs",
                columns: new[] { "ArticleId", "JobType" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_HeadingId",
                table: "ArticleGenerationJobs",
                column: "HeadingId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_Status_NextRunAt",
                table: "ArticleGenerationJobs",
                columns: new[] { "Status", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_Status_Priority_QueuedAt",
                table: "ArticleGenerationJobs",
                columns: new[] { "Status", "Priority", "QueuedAt" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_UserId_QueuedAt",
                table: "ArticleGenerationJobs",
                columns: new[] { "UserId", "QueuedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleHeadings_ArticleId_DisplayOrder",
                table: "ArticleHeadings",
                columns: new[] { "ArticleId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleHeadings_ArticleId_ParentId",
                table: "ArticleHeadings",
                columns: new[] { "ArticleId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleHeadings_Status",
                table: "ArticleHeadings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_AutoPostWordpressSiteId",
                table: "Articles",
                column: "AutoPostWordpressSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_DeletedAt",
                table: "Articles",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_HumanReviewedByUserId",
                table: "Articles",
                column: "HumanReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_Tags_Gin",
                table: "Articles",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_UserId_CreatedAt",
                table: "Articles",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_UserId_Status",
                table: "Articles",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_UserId_Title",
                table: "Articles",
                columns: new[] { "UserId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_Articles_WritingProfileWordpressSiteId",
                table: "Articles",
                column: "WritingProfileWordpressSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "Action", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_ArticleId",
                table: "NotificationLogs",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_JobId",
                table: "NotificationLogs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_NotificationSettingId",
                table: "NotificationLogs",
                column: "NotificationSettingId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_UserId_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_NotificationSettings_UserId_Provider_Active",
                table: "NotificationSettings",
                columns: new[] { "UserId", "Provider" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SearchResults_ArticleId_FetchedAt",
                table: "SearchResults",
                columns: new[] { "ArticleId", "FetchedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SearchResults_HeadingId",
                table: "SearchResults",
                column: "HeadingId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchResults_Query",
                table: "SearchResults",
                column: "Query");

            migrationBuilder.CreateIndex(
                name: "IX_SearchResults_QueryHash_CacheExpiresAt",
                table: "SearchResults",
                columns: new[] { "QueryHash", "CacheExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchResults_UserId",
                table: "SearchResults",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLedgers_ArticleId",
                table: "UsageLedgers",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLedgers_JobId",
                table: "UsageLedgers",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageLedgers_UserId_OccurredAt",
                table: "UsageLedgers",
                columns: new[] { "UserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_UserUsageLimits_UserId",
                table: "UserUsageLimits",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WordpressPosts_ArticleId_CreatedAt",
                table: "WordpressPosts",
                columns: new[] { "ArticleId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WordpressPosts_JobId",
                table: "WordpressPosts",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_WordpressPosts_UserId_CreatedAt",
                table: "WordpressPosts",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WordpressPosts_WordpressSiteId_CreatedAt",
                table: "WordpressPosts",
                columns: new[] { "WordpressSiteId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WordpressSites_UserId_CreatedAt",
                table: "WordpressSites",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WordpressSites_UserId_DeletedAt",
                table: "WordpressSites",
                columns: new[] { "UserId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_ArticleId_FetchedAt",
                table: "XSearchPosts",
                columns: new[] { "ArticleId", "FetchedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_HeadingId",
                table: "XSearchPosts",
                column: "HeadingId");

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_PostedAt",
                table: "XSearchPosts",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_QueryHash_CacheExpiresAt",
                table: "XSearchPosts",
                columns: new[] { "QueryHash", "CacheExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_UserId",
                table: "XSearchPosts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_XSearchPosts_PostId",
                table: "XSearchPosts",
                column: "PostId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGenerationLogs");

            migrationBuilder.DropTable(
                name: "AiModelSettings");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "SearchResults");

            migrationBuilder.DropTable(
                name: "UsageLedgers");

            migrationBuilder.DropTable(
                name: "UserUsageLimits");

            migrationBuilder.DropTable(
                name: "WordpressPosts");

            migrationBuilder.DropTable(
                name: "XSearchPosts");

            migrationBuilder.DropTable(
                name: "NotificationSettings");

            migrationBuilder.DropTable(
                name: "ArticleGenerationJobs");

            migrationBuilder.DropTable(
                name: "ArticleHeadings");

            migrationBuilder.DropTable(
                name: "Articles");

            migrationBuilder.DropTable(
                name: "WordpressSites");
        }
    }
}
