using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Wordpress;

public sealed class WordpressSiteService(
    ApplicationDbContext dbContext,
    ISecretProtector secretProtector,
    IUrlSafetyValidator urlSafetyValidator,
    IWordpressClient wordpressClient)
    : IWordpressSiteCommandService, IWordpressSiteQueryService
{
    public async Task<WordpressSiteListResponse> ListAsync(
        WordpressActor actor,
        CancellationToken cancellationToken = default)
    {
        var sites = await dbContext.WordpressSites
            .AsNoTracking()
            .Where(site => site.UserId == actor.UserId)
            .OrderBy(site => site.SiteName)
            .ThenByDescending(site => site.CreatedAt)
            .Select(site => ToResponse(site))
            .ToListAsync(cancellationToken);

        return new WordpressSiteListResponse(sites);
    }

    public async Task<WordpressServiceResult<WordpressSiteResponse>> CreateAsync(
        CreateWordpressSiteCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = await ValidateSiteInputAsync(
            command.SiteName,
            command.BaseUrl,
            command.LoginId,
            command.ApplicationPassword,
            applicationPasswordRequired: true,
            command.DefaultCategoryName,
            command.SiteAdminProfile,
            command.WritingCharacter,
            command.ReaderPersona,
            cancellationToken);

        if (validationErrors.Count > 0)
        {
            return WordpressServiceResult<WordpressSiteResponse>.Failure(
                WordpressServiceError.ValidationFailed,
                validationErrors);
        }

        var urlValidation = await urlSafetyValidator.ValidateHttpsPublicUrlAsync(
            command.BaseUrl,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var site = new WordpressSite
        {
            UserId = command.UserId,
            SiteName = command.SiteName.Trim(),
            BaseUrl = urlValidation.Uri!.ToString(),
            LoginId = command.LoginId.Trim(),
            EncryptedApplicationPassword = secretProtector.Protect(command.ApplicationPassword.Trim()),
            DefaultCategoryId = command.DefaultCategoryId,
            DefaultCategoryName = NormalizeOptionalText(command.DefaultCategoryName),
            SiteAdminProfile = NormalizeOptionalText(command.SiteAdminProfile),
            WritingCharacter = NormalizeOptionalText(command.WritingCharacter),
            ReaderPersona = NormalizeOptionalText(command.ReaderPersona),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.WordpressSites.Add(site);
        await dbContext.SaveChangesAsync(cancellationToken);

        return WordpressServiceResult<WordpressSiteResponse>.Success(ToResponse(site));
    }

    public async Task<WordpressServiceResult<WordpressSiteResponse>> UpdateAsync(
        UpdateWordpressSiteCommand command,
        CancellationToken cancellationToken = default)
    {
        var site = await dbContext.WordpressSites
            .FirstOrDefaultAsync(item => item.Id == command.WordpressSiteId, cancellationToken);

        if (site is null || !CanAccess(command.Actor, site.UserId))
        {
            return WordpressServiceResult<WordpressSiteResponse>.Failure(WordpressServiceError.NotFound);
        }

        var validationErrors = await ValidateSiteInputAsync(
            command.SiteName,
            command.BaseUrl,
            command.LoginId,
            command.ApplicationPassword,
            applicationPasswordRequired: false,
            command.DefaultCategoryName,
            command.SiteAdminProfile,
            command.WritingCharacter,
            command.ReaderPersona,
            cancellationToken);

        if (validationErrors.Count > 0)
        {
            return WordpressServiceResult<WordpressSiteResponse>.Failure(
                WordpressServiceError.ValidationFailed,
                validationErrors);
        }

        if (!string.IsNullOrWhiteSpace(command.RowVersion))
        {
            if (!TryDecodeRowVersion(command.RowVersion, out var rowVersion))
            {
                return WordpressServiceResult<WordpressSiteResponse>.Failure(
                    WordpressServiceError.ValidationFailed,
                    [new WordpressValidationError(nameof(command.RowVersion), "RowVersionが不正です。")]);
            }

            dbContext.Entry(site).Property(item => item.RowVersion).OriginalValue = rowVersion;
        }

        var urlValidation = await urlSafetyValidator.ValidateHttpsPublicUrlAsync(
            command.BaseUrl,
            cancellationToken);

        site.SiteName = command.SiteName.Trim();
        site.BaseUrl = urlValidation.Uri!.ToString();
        site.LoginId = command.LoginId.Trim();
        if (!string.IsNullOrWhiteSpace(command.ApplicationPassword))
        {
            site.EncryptedApplicationPassword = secretProtector.Protect(command.ApplicationPassword.Trim());
        }

        site.DefaultCategoryId = command.DefaultCategoryId;
        site.DefaultCategoryName = NormalizeOptionalText(command.DefaultCategoryName);
        site.SiteAdminProfile = NormalizeOptionalText(command.SiteAdminProfile);
        site.WritingCharacter = NormalizeOptionalText(command.WritingCharacter);
        site.ReaderPersona = NormalizeOptionalText(command.ReaderPersona);
        site.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WordpressServiceResult<WordpressSiteResponse>.Failure(WordpressServiceError.ConcurrencyConflict);
        }

        return WordpressServiceResult<WordpressSiteResponse>.Success(ToResponse(site));
    }

    public async Task<WordpressServiceResult> DeleteAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default)
    {
        var site = await dbContext.WordpressSites
            .FirstOrDefaultAsync(item => item.Id == wordpressSiteId, cancellationToken);

        if (site is null || !CanAccess(actor, site.UserId))
        {
            return WordpressServiceResult.Failure(WordpressServiceError.NotFound);
        }

        site.DeletedAt = DateTimeOffset.UtcNow;
        site.UpdatedAt = site.DeletedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return WordpressServiceResult.Success;
    }

    public async Task<WordpressServiceResult<WordpressConnectionTestResponse>> TestConnectionAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default)
    {
        var site = await dbContext.WordpressSites
            .FirstOrDefaultAsync(item => item.Id == wordpressSiteId, cancellationToken);

        if (site is null || !CanAccess(actor, site.UserId))
        {
            return WordpressServiceResult<WordpressConnectionTestResponse>.Failure(WordpressServiceError.NotFound);
        }

        try
        {
            var result = await wordpressClient.TestConnectionAsync(
                CreateConnection(site),
                cancellationToken);

            if (result.Success)
            {
                site.LastConnectedAt = result.CheckedAt;
                site.UpdatedAt = result.CheckedAt;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return WordpressServiceResult<WordpressConnectionTestResponse>.Success(
                new WordpressConnectionTestResponse(
                    result.Success,
                    result.Message,
                    result.CheckedAt));
        }
        catch (ExternalIntegrationException ex)
        {
            return WordpressServiceResult<WordpressConnectionTestResponse>.Failure(
                WordpressServiceError.ExternalFailure,
                [new WordpressValidationError("wordpress", ex.UserMessage)]);
        }
    }

    public async Task<WordpressServiceResult<WordpressCategoryListResponse>> GetCategoriesAsync(
        WordpressActor actor,
        Guid wordpressSiteId,
        CancellationToken cancellationToken = default)
    {
        var site = await dbContext.WordpressSites
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == wordpressSiteId, cancellationToken);

        if (site is null || !CanAccess(actor, site.UserId))
        {
            return WordpressServiceResult<WordpressCategoryListResponse>.Failure(WordpressServiceError.NotFound);
        }

        try
        {
            var categories = await wordpressClient.GetCategoriesAsync(
                CreateConnection(site),
                cancellationToken);

            return WordpressServiceResult<WordpressCategoryListResponse>.Success(
                new WordpressCategoryListResponse(categories));
        }
        catch (ExternalIntegrationException ex)
        {
            return WordpressServiceResult<WordpressCategoryListResponse>.Failure(
                WordpressServiceError.ExternalFailure,
                [new WordpressValidationError("wordpress", ex.UserMessage)]);
        }
    }

    private async Task<List<WordpressValidationError>> ValidateSiteInputAsync(
        string siteName,
        string baseUrl,
        string loginId,
        string? applicationPassword,
        bool applicationPasswordRequired,
        string? defaultCategoryName,
        string? siteAdminProfile,
        string? writingCharacter,
        string? readerPersona,
        CancellationToken cancellationToken)
    {
        var errors = new List<WordpressValidationError>();
        ValidateRequiredLength(nameof(siteName), siteName, 1, 100, errors);
        ValidateRequiredLength(nameof(loginId), loginId, 1, 100, errors);

        if (applicationPasswordRequired || !string.IsNullOrWhiteSpace(applicationPassword))
        {
            ValidateRequiredLength(nameof(applicationPassword), applicationPassword, 1, 300, errors);
        }

        ValidateMaxLength(nameof(defaultCategoryName), defaultCategoryName, 200, errors);
        ValidateMaxLength(nameof(siteAdminProfile), siteAdminProfile, 2000, errors);
        ValidateMaxLength(nameof(writingCharacter), writingCharacter, 3000, errors);
        ValidateMaxLength(nameof(readerPersona), readerPersona, 3000, errors);

        var urlValidation = await urlSafetyValidator.ValidateHttpsPublicUrlAsync(baseUrl, cancellationToken);
        if (!urlValidation.Succeeded)
        {
            errors.Add(new WordpressValidationError(nameof(baseUrl), urlValidation.ErrorMessage ?? "URLが不正です。"));
        }

        return errors;
    }

    private WordpressSiteConnection CreateConnection(WordpressSite site)
    {
        return new WordpressSiteConnection(
            site.BaseUrl,
            site.LoginId,
            secretProtector.Unprotect(site.EncryptedApplicationPassword));
    }

    private static WordpressSiteResponse ToResponse(WordpressSite site)
    {
        return new WordpressSiteResponse(
            site.Id,
            site.SiteName,
            site.BaseUrl,
            site.LoginId,
            site.DefaultCategoryId,
            site.DefaultCategoryName,
            site.SiteAdminProfile,
            site.WritingCharacter,
            site.ReaderPersona,
            site.LastConnectedAt,
            site.CreatedAt,
            site.UpdatedAt,
            Convert.ToBase64String(site.RowVersion));
    }

    private static void ValidateRequiredLength(
        string field,
        string? value,
        int min,
        int max,
        ICollection<WordpressValidationError> errors)
    {
        var length = value?.Trim().Length ?? 0;
        if (length < min || length > max)
        {
            errors.Add(new WordpressValidationError(field, $"{min}から{max}文字で入力してください。"));
        }
    }

    private static void ValidateMaxLength(
        string field,
        string? value,
        int max,
        ICollection<WordpressValidationError> errors)
    {
        if (!string.IsNullOrEmpty(value) && value.Trim().Length > max)
        {
            errors.Add(new WordpressValidationError(field, $"{max}文字以内で入力してください。"));
        }
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return ArticleInputNormalizer.NormalizeOptionalText(value);
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

    private static bool CanAccess(WordpressActor actor, string ownerUserId)
    {
        return actor.IsAdmin || string.Equals(actor.UserId, ownerUserId, StringComparison.Ordinal);
    }
}
