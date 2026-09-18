using System.Security.Cryptography;
using System.Text;
using WebWritingTool.Application.Search;

namespace WebWritingTool.Infrastructure.Search;

public sealed class DummySearchClient : IWebSearchClient, IXFullArchiveSearchClient
{
    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        WebSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueryKey(request.Query);
        return Task.FromResult<IReadOnlyList<WebSearchResult>>(Enumerable.Range(1, Math.Clamp(request.MaxResults, 1, 20))
            .Select(index => new WebSearchResult($"【サンプル】{request.Query}の参考情報 {index}",
                $"https://example.com/dummy/{key}/{index}",
                $"「{request.Query}」の検索・参考情報表示を確認するダミーデータです。実在する記事や調査結果ではありません。",
                index, "Dummy")).ToArray());
    }

    public Task<IReadOnlyList<XSearchPostResult>> SearchAsync(
        XFullArchiveSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = QueryKey(request.Query);
        return Task.FromResult<IReadOnlyList<XSearchPostResult>>(Enumerable.Range(1, Math.Clamp(request.MaxResults, 1, 100))
            .Select(index => new XSearchPostResult($"dummy-{key}-{index}", "sample-author",
                $"【サンプル投稿 {index}】「{request.Query}」に関する表示確認用の文章です。実在するX投稿ではありません。",
                null, "ja", DateTimeOffset.UtcNow.AddHours(-index))).ToArray());
    }

    public Task<IReadOnlyList<XSearchPostResult>> RehydrateAsync(
        XPostRehydrationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<XSearchPostResult>>([]);
    }

    private static string QueryKey(string query) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(SearchQueryNormalizer.NormalizeText(query)))).ToLowerInvariant()[..24];
}
