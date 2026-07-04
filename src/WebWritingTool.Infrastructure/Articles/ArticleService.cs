using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

public sealed class ArticleService(
    ApplicationDbContext dbContext,
    IContentRenderingService contentRenderingService,
    IUrlSafetyValidator urlSafetyValidator,
    ISecurityRateLimiter securityRateLimiter,
    ITopicRiskClassifier topicRiskClassifier)
    : IArticleCommandService, IArticleQueryService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private const string DefaultNotificationMode = "None";

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ArticleListResponse> SearchAsync(
        ArticleActor actor,
        ArticleListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, DefaultPage);
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        var articles = dbContext.Articles.AsNoTracking();
        if (!actor.IsAdmin)
        {
            articles = articles.Where(article => article.UserId == actor.UserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim()}%";
            articles = articles.Where(article =>
                EF.Functions.ILike(article.Keyword, pattern)
                || (article.Title != null && EF.Functions.ILike(article.Title, pattern))
                || (article.Memo != null && EF.Functions.ILike(article.Memo, pattern)));
        }

        foreach (var tag in ArticleInputNormalizer.NormalizeTags(query.Tags))
        {
            articles = articles.Where(article => article.Tags.Contains(tag));
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<ArticleStatus>(query.Status, ignoreCase: true, out var status))
        {
            articles = articles.Where(article => article.Status == status);
        }

        if (query.CreatedFrom.HasValue)
        {
            var createdFrom = ToUtcStart(query.CreatedFrom.Value);
            articles = articles.Where(article => article.CreatedAt >= createdFrom);
        }

        if (query.CreatedTo.HasValue)
        {
            var createdToExclusive = ToUtcStart(query.CreatedTo.Value.AddDays(1));
            articles = articles.Where(article => article.CreatedAt < createdToExclusive);
        }

        articles = ApplySort(articles, query.Sort, query.Direction);

        var totalCount = await articles.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var pageArticles = await articles
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(article => new ArticleListProjection(
                article.Id,
                article.CreatedAt,
                article.OutlineMethod,
                article.SearchMode,
                article.Status,
                article.Title,
                article.Keyword,
                article.Tags,
                article.Memo,
                article.GenerationModel))
            .ToListAsync(cancellationToken);

        var jobStates = await GetJobStatesAsync(
            pageArticles.Select(article => article.Id).ToArray(),
            cancellationToken);

        var items = pageArticles
            .Select(article =>
            {
                jobStates.TryGetValue(article.Id, out var jobState);
                return new ArticleListItemResponse(
                    article.Id,
                    article.CreatedAt,
                    GetHeadlineSource(article.OutlineMethod, article.SearchMode),
                    article.Status.ToString(),
                    GetStatusLabel(article.Status),
                    article.Title,
                    article.Keyword,
                    article.Tags,
                    article.Memo,
                    article.GenerationModel,
                    article.Status.CanPostToWordpress(),
                    jobState?.HasRunningJob ?? false,
                    jobState?.HasQueuedJob ?? false);
            })
            .ToArray();

        return new ArticleListResponse(
            items,
            page,
            pageSize,
            totalCount,
            totalPages,
            page > 1,
            totalPages > 0 && page < totalPages);
    }

    public async Task<ArticleDetailResponse?> GetAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return null;
        }

        return await ToDetailResponseAsync(article, cancellationToken);
    }

    public async Task<ArticleFormOptionsResponse> GetFormOptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var generationModels = await dbContext.AiModelSettings
            .AsNoTracking()
            .Where(model => model.Enabled)
            .OrderBy(model => model.SortOrder)
            .ThenBy(model => model.DisplayName)
            .Select(model => new ArticleGenerationModelOption(
                model.Model,
                model.DisplayName,
                model.Provider))
            .ToListAsync(cancellationToken);

        var writingProfiles = await dbContext.WordpressSites
            .AsNoTracking()
            .Where(site => site.UserId == userId)
            .OrderBy(site => site.SiteName)
            .Select(site => new WritingProfileOption(
                site.Id,
                site.SiteName,
                site.DefaultCategoryId,
                site.DefaultCategoryName))
            .ToListAsync(cancellationToken);

        return new ArticleFormOptionsResponse(generationModels, writingProfiles);
    }

    public async Task<ArticleServiceResult<CreateArticleResponse>> CreateAsync(
        CreateArticleCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = await ValidateCreateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return ArticleServiceResult<CreateArticleResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                validationErrors);
        }

        var writingProfile = await ResolveWritingProfileAsync(
            command.UserId,
            command.WritingProfileWordpressSiteId,
            cancellationToken);

        if (command.WritingProfileWordpressSiteId.HasValue && writingProfile is null)
        {
            return ArticleServiceResult<CreateArticleResponse>.Failure(ArticleServiceError.NotFound);
        }

        var article = new Article
        {
            UserId = command.UserId,
            Keyword = command.Keyword.Trim(),
            Title = ArticleInputNormalizer.NormalizeOptionalText(command.Title),
            Status = ArticleStatus.Draft,
            Tone = ArticleInputNormalizer.NormalizeOptionalText(command.Tone),
            Tags = ArticleInputNormalizer.NormalizeTags(command.Tags).ToArray(),
            Memo = ArticleInputNormalizer.NormalizeOptionalText(command.Memo),
            SuggestedKeywords = ArticleInputNormalizer.NormalizeOptionalText(command.SuggestedKeywords),
            RelatedKeywords = ArticleInputNormalizer.NormalizeOptionalText(command.RelatedKeywords),
            LearningType = NormalizeLearningType(command.LearningType),
            LearningText = ArticleInputNormalizer.NormalizeOptionalText(command.LearningText),
            AdditionalPrompt = ArticleInputNormalizer.NormalizeOptionalText(command.AdditionalPrompt),
            GenerationModel = command.GenerationModel.Trim(),
            OutlineMethod = command.OutlineMethod.Trim(),
            SearchMode = command.SearchMode,
            IsDomesticOnly = command.IsDomesticOnly,
            NotificationMode = NormalizeNotificationMode(command.NotificationMode),
            WritingProfileWordpressSiteId = writingProfile?.Id,
            WritingProfileSnapshotJson = writingProfile is null ? null : CreateWritingProfileSnapshotJson(writingProfile)
        };

        ApplyTopicRisk(article);

        dbContext.Articles.Add(article);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult<CreateArticleResponse>.Success(
            new CreateArticleResponse(article.Id, article.Status.ToString(), $"/api/articles/{article.Id}"));
    }

    public async Task<ArticleServiceResult<BulkCreateArticlesResponse>> BulkCreateAsync(
        BulkCreateArticlesCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!await securityRateLimiter.IsAllowedAsync(
                SecurityRateLimitPolicyNames.BulkArticleRegistration,
                command.UserId,
                cancellationToken))
        {
            return ArticleServiceResult<BulkCreateArticlesResponse>.Failure(ArticleServiceError.RateLimited);
        }

        var validationErrors = await ValidateBulkCreateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return ArticleServiceResult<BulkCreateArticlesResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                validationErrors);
        }

        var parsedLines = new List<BulkArticleLine>();
        var rejectedLines = new List<BulkArticleRejectedLine>();

        for (var index = 0; index < command.Lines.Count; index++)
        {
            var parseResult = ArticleInputNormalizer.ParseBulkLine(command.Lines[index], index + 1);
            if (parseResult.ArticleLine is not null)
            {
                var lineErrors = ValidateBulkArticleLine(parseResult.ArticleLine);
                if (lineErrors.Count == 0)
                {
                    parsedLines.Add(parseResult.ArticleLine);
                }
                else
                {
                    rejectedLines.Add(new BulkArticleRejectedLine(
                        parseResult.ArticleLine.LineNumber,
                        command.Lines[index],
                        string.Join(" ", lineErrors.Select(error => error.Message))));
                }
            }
            else if (parseResult.RejectedLine is not null)
            {
                rejectedLines.Add(parseResult.RejectedLine);
            }
        }

        if (parsedLines.Count == 0)
        {
            rejectedLines.Add(new BulkArticleRejectedLine(0, string.Empty, "登録可能な行がありません。"));
            return ArticleServiceResult<BulkCreateArticlesResponse>.Success(
                new BulkCreateArticlesResponse(0, command.AutoPostToWordpress, [], rejectedLines));
        }

        var autoPostSite = await ResolveAutoPostSiteAsync(command, cancellationToken);
        if (command.AutoPostToWordpress && autoPostSite is null)
        {
            return ArticleServiceResult<BulkCreateArticlesResponse>.Failure(ArticleServiceError.NotFound);
        }

        var writingProfileSiteId = command.WritingProfileWordpressSiteId
            ?? (command.AutoPostToWordpress ? command.AutoPostWordpressSiteId : null);

        var writingProfile = await ResolveWritingProfileAsync(
            command.UserId,
            writingProfileSiteId,
            cancellationToken);

        if (writingProfileSiteId.HasValue && writingProfile is null)
        {
            return ArticleServiceResult<BulkCreateArticlesResponse>.Failure(ArticleServiceError.NotFound);
        }

        var articles = parsedLines
            .Select(line => new Article
            {
                UserId = command.UserId,
                Keyword = line.Keyword.Trim(),
                Title = ArticleInputNormalizer.NormalizeOptionalText(line.Title),
                Status = ArticleStatus.Draft,
                GenerationModel = command.GenerationModel.Trim(),
                OutlineMethod = command.OutlineMethod.Trim(),
                SearchMode = command.SearchMode,
                IsDomesticOnly = command.IsDomesticOnly,
                NotificationMode = DefaultNotificationMode,
                WritingProfileWordpressSiteId = writingProfile?.Id,
                WritingProfileSnapshotJson = writingProfile is null ? null : CreateWritingProfileSnapshotJson(writingProfile),
                AutoPostToWordpress = command.AutoPostToWordpress,
                AutoPostWordpressSiteId = autoPostSite?.Id,
                AutoPostWordpressCategoryId = command.AutoPostWordpressCategoryId ?? autoPostSite?.DefaultCategoryId
            })
            .ToArray();

        foreach (var article in articles)
        {
            ApplyTopicRisk(article);
        }

        dbContext.Articles.AddRange(articles);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ArticleServiceResult<BulkCreateArticlesResponse>.Success(
            new BulkCreateArticlesResponse(articles.Length, command.AutoPostToWordpress, [], rejectedLines));
    }

    public async Task<ArticleServiceResult<ArticleDetailResponse>> UpdateAsync(
        UpdateArticleCommand command,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == command.ArticleId, cancellationToken);

        if (article is null || !CanAccess(command.Actor, article.UserId))
        {
            return ArticleServiceResult<ArticleDetailResponse>.Failure(ArticleServiceError.NotFound);
        }

        var validationErrors = await ValidateUpdateAsync(command, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return ArticleServiceResult<ArticleDetailResponse>.Failure(
                ArticleServiceError.ValidationFailed,
                validationErrors);
        }

        var writingProfile = await ResolveWritingProfileAsync(
            article.UserId,
            command.WritingProfileWordpressSiteId,
            cancellationToken);

        if (command.WritingProfileWordpressSiteId.HasValue && writingProfile is null)
        {
            return ArticleServiceResult<ArticleDetailResponse>.Failure(ArticleServiceError.NotFound);
        }

        if (!string.IsNullOrWhiteSpace(command.RowVersion))
        {
            if (!TryDecodeRowVersion(command.RowVersion, out var rowVersion))
            {
                return ArticleServiceResult<ArticleDetailResponse>.Failure(
                    ArticleServiceError.ValidationFailed,
                    [new ArticleValidationError(nameof(command.RowVersion), "RowVersionが不正です。")]);
            }

            dbContext.Entry(article).Property(item => item.RowVersion).OriginalValue = rowVersion;
        }

        var normalizedBody = ArticleInputNormalizer.NormalizeOptionalText(command.Body);
        var normalizedHtmlBody = ArticleInputNormalizer.NormalizeOptionalText(command.HtmlBody);
        var sanitizedHtmlBody = normalizedHtmlBody is null
            ? null
            : ArticleInputNormalizer.NormalizeOptionalText(contentRenderingService.SanitizeHtml(normalizedHtmlBody));

        var reviewSensitiveValueChanged =
            !string.Equals(article.Title, command.Title.Trim(), StringComparison.Ordinal)
            || !string.Equals(article.Body, normalizedBody, StringComparison.Ordinal)
            || !string.Equals(article.HtmlBody, sanitizedHtmlBody, StringComparison.Ordinal)
            || !string.Equals(article.MetaDescription, ArticleInputNormalizer.NormalizeOptionalText(command.MetaDescription), StringComparison.Ordinal);

        article.Keyword = command.Keyword.Trim();
        article.Title = command.Title.Trim();
        article.Tags = ArticleInputNormalizer.NormalizeTags(command.Tags).ToArray();
        article.Memo = ArticleInputNormalizer.NormalizeOptionalText(command.Memo);
        article.Tone = ArticleInputNormalizer.NormalizeOptionalText(command.Tone);
        article.SuggestedKeywords = ArticleInputNormalizer.NormalizeOptionalText(command.SuggestedKeywords);
        article.RelatedKeywords = ArticleInputNormalizer.NormalizeOptionalText(command.RelatedKeywords);
        article.LearningType = NormalizeLearningType(command.LearningType);
        article.LearningText = ArticleInputNormalizer.NormalizeOptionalText(command.LearningText);
        article.AdditionalPrompt = ArticleInputNormalizer.NormalizeOptionalText(command.AdditionalPrompt);
        article.MetaDescription = ArticleInputNormalizer.NormalizeOptionalText(command.MetaDescription);
        article.GenerationModel = ArticleInputNormalizer.NormalizeOptionalText(command.GenerationModel);
        article.OutlineMethod = ArticleInputNormalizer.NormalizeOptionalText(command.OutlineMethod) ?? article.OutlineMethod;
        article.SearchMode = command.SearchMode;
        article.IsDomesticOnly = command.IsDomesticOnly;
        article.NotificationMode = NormalizeNotificationMode(command.NotificationMode);
        article.WritingProfileWordpressSiteId = writingProfile?.Id;
        article.WritingProfileSnapshotJson = writingProfile is null ? null : CreateWritingProfileSnapshotJson(writingProfile);
        article.Body = normalizedBody;
        article.HtmlBody = sanitizedHtmlBody;

        var topicRiskEscalated = ApplyTopicRisk(article);

        if (reviewSensitiveValueChanged || topicRiskEscalated)
        {
            article.InvalidateHumanReview();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ArticleServiceResult<ArticleDetailResponse>.Failure(ArticleServiceError.ConcurrencyConflict);
        }

        var detail = await ToDetailResponseAsync(article, cancellationToken);
        return ArticleServiceResult<ArticleDetailResponse>.Success(detail);
    }

    public async Task<ArticleServiceResult> DeleteAsync(
        ArticleActor actor,
        Guid articleId,
        CancellationToken cancellationToken = default)
    {
        var article = await dbContext.Articles
            .FirstOrDefaultAsync(item => item.Id == articleId, cancellationToken);

        if (article is null || !CanAccess(actor, article.UserId))
        {
            return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        }

        var hasRunningJob = await dbContext.ArticleGenerationJobs.AnyAsync(
            job => job.ArticleId == articleId && job.Status == JobStatus.Running,
            cancellationToken);

        if (hasRunningJob)
        {
            return ArticleServiceResult.Failure(ArticleServiceError.ConflictRunningJob);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var queuedJobs = await dbContext.ArticleGenerationJobs
            .Where(job => job.ArticleId == articleId && job.Status == JobStatus.Queued)
            .ToListAsync(cancellationToken);

        foreach (var job in queuedJobs)
        {
            job.Status = JobStatus.Canceled;
            job.CanceledAt = now;
            job.FinishedAt ??= now;
        }

        article.DeletedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ArticleServiceResult.Success;
    }

    private async Task<IReadOnlyList<ArticleValidationError>> ValidateCreateAsync(
        CreateArticleCommand command,
        CancellationToken cancellationToken)
    {
        var errors = ValidateArticleInput(
            command.Keyword,
            command.Title,
            requireTitle: false,
            command.GenerateImage,
            command.H2Count,
            command.H3Count,
            command.Tone,
            command.Tags,
            command.Memo,
            command.SuggestedKeywords,
            command.RelatedKeywords,
            command.LearningType,
            command.LearningText,
            command.AdditionalPrompt,
            command.OutlineMethod,
            command.GenerationModel,
            command.NotificationMode);

        await ValidateGenerationModelAsync(command.GenerationModel, errors, cancellationToken);
        await ValidateLearningUrlAsync(command.LearningType, command.LearningText, errors, cancellationToken);
        return errors;
    }

    private async Task<IReadOnlyList<ArticleValidationError>> ValidateBulkCreateAsync(
        BulkCreateArticlesCommand command,
        CancellationToken cancellationToken)
    {
        var errors = new List<ArticleValidationError>();

        if (command.Lines.Count == 0)
        {
            errors.Add(new ArticleValidationError(nameof(command.Lines), "キーワード一覧を入力してください。"));
        }

        ValidateCount(nameof(command.H2Count), command.H2Count, 1, 20, errors);
        ValidateCount(nameof(command.H3Count), command.H3Count, 0, 60, errors);

        if (string.IsNullOrWhiteSpace(command.TitleMethod))
        {
            errors.Add(new ArticleValidationError(nameof(command.TitleMethod), "タイトル構築方法を選択してください。"));
        }

        ValidateRequiredOption(nameof(command.OutlineMethod), command.OutlineMethod, ["Keyword", "Search", "Ai"], errors);

        if (string.IsNullOrWhiteSpace(command.GenerationModel))
        {
            errors.Add(new ArticleValidationError(nameof(command.GenerationModel), "生成モデルを選択してください。"));
        }
        else
        {
            await ValidateGenerationModelAsync(command.GenerationModel, errors, cancellationToken);
        }

        if (command.AutoPostToWordpress && !command.AutoPostWordpressSiteId.HasValue)
        {
            errors.Add(new ArticleValidationError(nameof(command.AutoPostWordpressSiteId), "WordPress自動投稿先サイトを選択してください。"));
        }

        return errors;
    }

    private async Task<IReadOnlyList<ArticleValidationError>> ValidateUpdateAsync(
        UpdateArticleCommand command,
        CancellationToken cancellationToken)
    {
        var errors = ValidateArticleInput(
            command.Keyword,
            command.Title,
            requireTitle: true,
            generateImage: false,
            h2Count: null,
            h3Count: null,
            command.Tone,
            command.Tags,
            command.Memo,
            command.SuggestedKeywords,
            command.RelatedKeywords,
            command.LearningType,
            command.LearningText,
            command.AdditionalPrompt,
            command.OutlineMethod,
            command.GenerationModel,
            command.NotificationMode);

        ValidateMaxLength(nameof(command.MetaDescription), command.MetaDescription, 320, errors);

        if (!string.IsNullOrWhiteSpace(command.GenerationModel))
        {
            await ValidateGenerationModelAsync(command.GenerationModel, errors, cancellationToken);
        }

        await ValidateLearningUrlAsync(command.LearningType, command.LearningText, errors, cancellationToken);
        return errors;
    }

    private static List<ArticleValidationError> ValidateArticleInput(
        string keyword,
        string? title,
        bool requireTitle,
        bool generateImage,
        int? h2Count,
        int? h3Count,
        string? tone,
        IReadOnlyList<string> tags,
        string? memo,
        string? suggestedKeywords,
        string? relatedKeywords,
        string? learningType,
        string? learningText,
        string? additionalPrompt,
        string? outlineMethod,
        string? generationModel,
        string? notificationMode)
    {
        var errors = new List<ArticleValidationError>();

        ValidateRequiredLength(nameof(keyword), keyword, 1, 200, errors);

        if (requireTitle)
        {
            ValidateRequiredLength(nameof(title), title, 1, 250, errors);
        }
        else
        {
            ValidateMaxLength(nameof(title), title, 250, errors);
        }

        if (generateImage)
        {
            errors.Add(new ArticleValidationError(nameof(generateImage), "MVPでは画像生成を利用できません。"));
        }

        ValidateCount(nameof(h2Count), h2Count, 1, 20, errors);
        ValidateCount(nameof(h3Count), h3Count, 0, 60, errors);
        ValidateMaxLength(nameof(tone), tone, 40, errors);
        ValidateTags(tags, errors);
        ValidateMaxLength(nameof(memo), memo, 1000, errors);
        ValidateMaxLength(nameof(suggestedKeywords), suggestedKeywords, 10000, errors);
        ValidateMaxLength(nameof(relatedKeywords), relatedKeywords, 10000, errors);
        ValidateRequiredOption(nameof(outlineMethod), outlineMethod, ["Keyword", "Search", "Ai"], errors);
        ValidateMaxLength(nameof(additionalPrompt), additionalPrompt, 3000, errors);
        ValidateRequiredOption(nameof(notificationMode), notificationMode ?? DefaultNotificationMode, ["None", "Discord"], errors);

        if (string.IsNullOrWhiteSpace(generationModel))
        {
            errors.Add(new ArticleValidationError(nameof(generationModel), "生成モデルを選択してください。"));
        }

        if (!string.IsNullOrWhiteSpace(learningType))
        {
            ValidateRequiredOption(nameof(learningType), learningType, ["None", "Text", "Url"], errors);
        }

        if (string.Equals(learningType, "Url", StringComparison.OrdinalIgnoreCase)
            && !IsHttpsUrl(learningText))
        {
            errors.Add(new ArticleValidationError(nameof(learningText), "事前学習URLはHTTPS URLを指定してください。"));
        }

        return errors;
    }

    private static IReadOnlyList<ArticleValidationError> ValidateBulkArticleLine(BulkArticleLine line)
    {
        var errors = new List<ArticleValidationError>();
        ValidateRequiredLength(nameof(line.Keyword), line.Keyword, 1, 200, errors);
        ValidateMaxLength(nameof(line.Title), line.Title, 250, errors);
        return errors;
    }

    private async Task ValidateGenerationModelAsync(
        string generationModel,
        ICollection<ArticleValidationError> errors,
        CancellationToken cancellationToken)
    {
        var model = generationModel.Trim();
        var exists = await dbContext.AiModelSettings.AnyAsync(
            setting => setting.Enabled && setting.Model == model,
            cancellationToken);

        if (!exists)
        {
            errors.Add(new ArticleValidationError(nameof(generationModel), "利用可能な生成モデルを選択してください。"));
        }
    }

    private static void ValidateRequiredLength(
        string field,
        string? value,
        int min,
        int max,
        ICollection<ArticleValidationError> errors)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < min || length > max)
        {
            errors.Add(new ArticleValidationError(field, $"{min}から{max}文字で入力してください。"));
        }
    }

    private static void ValidateMaxLength(
        string field,
        string? value,
        int max,
        ICollection<ArticleValidationError> errors)
    {
        if (!string.IsNullOrEmpty(value) && value.Trim().Length > max)
        {
            errors.Add(new ArticleValidationError(field, $"{max}文字以内で入力してください。"));
        }
    }

    private static void ValidateCount(
        string field,
        int? value,
        int min,
        int max,
        ICollection<ArticleValidationError> errors)
    {
        if (value.HasValue && (value.Value < min || value.Value > max))
        {
            errors.Add(new ArticleValidationError(field, $"{min}から{max}の範囲で指定してください。"));
        }
    }

    private static void ValidateRequiredOption(
        string field,
        string? value,
        IReadOnlyCollection<string> allowedValues,
        ICollection<ArticleValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !allowedValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new ArticleValidationError(field, $"指定できる値は {string.Join(", ", allowedValues)} です。"));
        }
    }

    private static void ValidateTags(IReadOnlyList<string> tags, ICollection<ArticleValidationError> errors)
    {
        foreach (var tag in ArticleInputNormalizer.NormalizeTags(tags))
        {
            if (tag.Length > 50)
            {
                errors.Add(new ArticleValidationError(nameof(tags), "タグは各50文字以内で入力してください。"));
                return;
            }
        }
    }

    private static bool IsHttpsUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ValidateLearningUrlAsync(
        string? learningType,
        string? learningText,
        ICollection<ArticleValidationError> errors,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(learningType, "Url", StringComparison.OrdinalIgnoreCase)
            || !IsHttpsUrl(learningText))
        {
            return;
        }

        var result = await urlSafetyValidator.ValidateHttpsPublicUrlAsync(learningText, cancellationToken);
        if (!result.Succeeded)
        {
            errors.Add(new ArticleValidationError(
                nameof(learningText),
                result.ErrorMessage ?? "事前学習URLが不正です。"));
        }
    }

    private static IQueryable<Article> ApplySort(
        IQueryable<Article> query,
        string? sort,
        string? direction)
    {
        var ascending = string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase);

        return sort?.ToLowerInvariant() switch
        {
            "title" => ascending
                ? query.OrderBy(article => article.Title).ThenByDescending(article => article.CreatedAt)
                : query.OrderByDescending(article => article.Title).ThenByDescending(article => article.CreatedAt),
            "status" => ascending
                ? query.OrderBy(article => article.Status).ThenByDescending(article => article.CreatedAt)
                : query.OrderByDescending(article => article.Status).ThenByDescending(article => article.CreatedAt),
            _ => ascending
                ? query.OrderBy(article => article.CreatedAt)
                : query.OrderByDescending(article => article.CreatedAt)
        };
    }

    private async Task<Dictionary<Guid, ArticleJobState>> GetJobStatesAsync(
        IReadOnlyCollection<Guid> articleIds,
        CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return [];
        }

        var jobs = await dbContext.ArticleGenerationJobs
            .AsNoTracking()
            .Where(job => job.ArticleId.HasValue
                && articleIds.Contains(job.ArticleId.Value)
                && (job.Status == JobStatus.Running || job.Status == JobStatus.Queued))
            .Select(job => new { ArticleId = job.ArticleId!.Value, job.Status })
            .ToListAsync(cancellationToken);

        return jobs
            .GroupBy(job => job.ArticleId)
            .ToDictionary(
                group => group.Key,
                group => new ArticleJobState(
                    group.Any(job => job.Status == JobStatus.Running),
                    group.Any(job => job.Status == JobStatus.Queued)));
    }

    private async Task<ArticleDetailResponse> ToDetailResponseAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        var writingProfileSiteName = article.WritingProfileWordpressSiteId.HasValue
            ? await dbContext.WordpressSites
                .AsNoTracking()
                .Where(site => site.Id == article.WritingProfileWordpressSiteId.Value)
                .Select(site => site.SiteName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var headings = await dbContext.ArticleHeadings
            .AsNoTracking()
            .Where(heading => heading.ArticleId == article.Id)
            .OrderBy(heading => heading.DisplayOrder)
            .Select(heading => new ArticleHeadingResponse(
                heading.Id,
                heading.ParentId,
                heading.Level,
                heading.Title,
                heading.Body,
                heading.DisplayOrder,
                heading.TargetLength,
                heading.ActualLength,
                heading.Status.ToString(),
                heading.UseWebSearch,
                Convert.ToBase64String(heading.RowVersion)))
            .ToListAsync(cancellationToken);

        return new ArticleDetailResponse(
            article.Id,
            article.Keyword,
            article.Title,
            article.Status.ToString(),
            article.Tone,
            article.Tags,
            article.Memo,
            article.SuggestedKeywords,
            article.RelatedKeywords,
            article.LearningType,
            article.LearningText,
            article.AdditionalPrompt,
            article.Body,
            article.HtmlBody,
            article.MetaDescription,
            article.GenerationModel,
            article.OutlineMethod,
            article.SearchMode,
            article.IsDomesticOnly,
            article.NotificationMode,
            article.TopicRisk,
            article.HumanReviewRequired,
            article.HumanReviewedAt,
            article.HumanReviewedByUserId,
            article.WritingProfileWordpressSiteId,
            writingProfileSiteName,
            article.AutoPostToWordpress,
            article.AutoPostWordpressSiteId,
            article.AutoPostWordpressCategoryId,
            article.CreatedAt,
            article.UpdatedAt,
            Convert.ToBase64String(article.RowVersion),
            headings);
    }

    private async Task<WordpressSite?> ResolveWritingProfileAsync(
        string userId,
        Guid? wordpressSiteId,
        CancellationToken cancellationToken)
    {
        if (!wordpressSiteId.HasValue)
        {
            return null;
        }

        return await dbContext.WordpressSites.FirstOrDefaultAsync(
            site => site.Id == wordpressSiteId.Value && site.UserId == userId,
            cancellationToken);
    }

    private async Task<WordpressSite?> ResolveAutoPostSiteAsync(
        BulkCreateArticlesCommand command,
        CancellationToken cancellationToken)
    {
        if (!command.AutoPostToWordpress || !command.AutoPostWordpressSiteId.HasValue)
        {
            return null;
        }

        return await dbContext.WordpressSites.FirstOrDefaultAsync(
            site => site.Id == command.AutoPostWordpressSiteId.Value && site.UserId == command.UserId,
            cancellationToken);
    }

    private bool ApplyTopicRisk(Article article)
    {
        var classification = topicRiskClassifier.Classify(
            article.Keyword,
            article.Title,
            article.Tags.Length == 0 ? null : string.Join(" ", article.Tags),
            article.Memo,
            article.SuggestedKeywords,
            article.RelatedKeywords,
            article.AdditionalPrompt);

        return article.ApplyTopicRiskEscalation(classification);
    }

    private static string CreateWritingProfileSnapshotJson(WordpressSite site)
    {
        var snapshot = new WritingProfileSnapshot(
            site.Id,
            site.SiteName,
            site.SiteAdminProfile,
            site.WritingCharacter,
            site.ReaderPersona);

        return JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
    }

    private static bool CanAccess(ArticleActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }

    private static string GetHeadlineSource(string outlineMethod, bool searchMode)
    {
        if (searchMode || string.Equals(outlineMethod, "Search", StringComparison.OrdinalIgnoreCase))
        {
            return "WebSearch";
        }

        return string.Equals(outlineMethod, "Ai", StringComparison.OrdinalIgnoreCase)
            ? "Gemini"
            : "Keyword";
    }

    private static string GetStatusLabel(ArticleStatus status)
    {
        return status switch
        {
            ArticleStatus.Draft => "下書き",
            ArticleStatus.OutlineQueued => "構成待機",
            ArticleStatus.OutlineGenerating => "構成生成中",
            ArticleStatus.OutlineReady => "構成完了",
            ArticleStatus.BodyQueued => "本文待機",
            ArticleStatus.BodyGenerating => "本文生成中",
            ArticleStatus.Completed => "完了",
            ArticleStatus.Posted => "投稿済み",
            ArticleStatus.Failed => "失敗",
            _ => status.ToString()
        };
    }

    private static DateTimeOffset ToUtcStart(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static string? NormalizeLearningType(string? learningType)
    {
        var normalized = ArticleInputNormalizer.NormalizeOptionalText(learningType);
        return normalized is null || string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string NormalizeNotificationMode(string? notificationMode)
    {
        return ArticleInputNormalizer.NormalizeOptionalText(notificationMode) ?? DefaultNotificationMode;
    }

    private static bool TryDecodeRowVersion(string rowVersion, out byte[] value)
    {
        try
        {
            value = Convert.FromBase64String(rowVersion);
            return value.Length > 0;
        }
        catch (FormatException)
        {
            value = [];
            return false;
        }
    }

    private sealed record ArticleListProjection(
        Guid Id,
        DateTimeOffset CreatedAt,
        string OutlineMethod,
        bool SearchMode,
        ArticleStatus Status,
        string? Title,
        string Keyword,
        string[] Tags,
        string? Memo,
        string? GenerationModel);

    private sealed record ArticleJobState(bool HasRunningJob, bool HasQueuedJob);

    private sealed record WritingProfileSnapshot(
        Guid WordpressSiteId,
        string SiteName,
        string? SiteAdminProfile,
        string? WritingCharacter,
        string? ReaderPersona);
}
