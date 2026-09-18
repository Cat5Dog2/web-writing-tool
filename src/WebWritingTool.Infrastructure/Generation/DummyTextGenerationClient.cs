using System.Net;
using System.Text.Json;
using WebWritingTool.Application.Generation;

namespace WebWritingTool.Infrastructure.Generation;

public sealed class DummyTextGenerationClient : IAiTextGenerationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var keyword = ReadField(request.UserPrompt, "キーワード")
            ?? ReadField(request.UserPrompt, "記事キーワード") ?? "サンプル記事";
        var text = request.Operation switch
        {
            AiOperations.TitleGeneration => CreateTitles(keyword, request.UserPrompt),
            AiOperations.OutlineGeneration => CreateOutline(keyword, request.UserPrompt),
            AiOperations.BodyGeneration or AiOperations.Rewrite or AiOperations.Summarize
                or AiOperations.Expand or AiOperations.Refresh => CreateBody(keyword, request),
            _ => throw new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.ValidationError,
                "ダミーモードで未対応の生成操作です。")
        };

        if (request.References.Count > 0 && request.Operation is not (AiOperations.TitleGeneration or AiOperations.OutlineGeneration))
        {
            text += "\n\n参考情報（動作確認用）:\n" + string.Join("\n", request.References.Select(
                reference => $"- {WebUtility.HtmlEncode(reference.Title)}"));
        }

        return Task.FromResult(new AiTextGenerationResult(
            text, "Dummy", "dummy-text", request.PromptChars, text.Length, null));
    }

    private static string CreateTitles(string keyword, string prompt)
    {
        var count = ReadCount(prompt, "候補数", 5, 1, 20);
        return JsonSerializer.Serialize(new
        {
            candidates = Enumerable.Range(1, count).Select(index => new TitleCandidate(
                $"【ダミー】{keyword}の基本と実践ポイント {index}", "動作確認用のタイトル候補です。"))
        }, JsonOptions);
    }

    private static string CreateOutline(string keyword, string prompt)
    {
        var h2Count = ReadCount(prompt, "H2数目安", 5, 1, 20);
        var h3Count = ReadCount(prompt, "H3数目安", 12, 0, 60);
        var headings = Enumerable.Range(0, h2Count).Select(index => new OutlineHeadingItem(
            2, $"【ダミー】{keyword}のポイント {index + 1}", 800,
            Enumerable.Range(1, h3Count / h2Count + (index < h3Count % h2Count ? 1 : 0))
                .Select(child => new OutlineHeadingItem(
                    3, $"{keyword}の確認事項 {index + 1}-{child}", 400, []))
                .ToArray())).ToArray();
        return JsonSerializer.Serialize(new OutlineGenerationResult(
            $"ダミーモードで作成した{keyword}の記事です。", headings), JsonOptions);
    }

    private static string CreateBody(string keyword, AiTextGenerationRequest request)
    {
        var heading = ReadField(request.UserPrompt, "対象見出し") ?? keyword;
        var operation = request.Operation switch
        {
            AiOperations.Rewrite => "リライト",
            AiOperations.Summarize => "要約",
            AiOperations.Expand => "長文化",
            AiOperations.Refresh => "更新",
            _ => "本文生成"
        };
        var introduction = $"【ダミー：{operation}】これは動作確認用のサンプル本文です。実際の調査やAI生成は行っていません。";
        var topic = $"「{WebUtility.HtmlEncode(keyword)}」について、「{WebUtility.HtmlEncode(heading)}」の内容をここに記載します。";
        if (request.Operation == AiOperations.Summarize)
        {
            return $"{introduction}\n\n{topic}";
        }

        var text = $"{introduction}\n\n{topic}\n\n"
            + "目的と前提を整理し、確認した情報を読み手に伝わる順序でまとめます。実際の記事では、根拠を確認した説明や具体例へ差し替えてください。\n\n"
            + "- 想定する読者と目的を確認する\n- 必要な情報と出典を整理する\n- 内容を確認してから公開する";
        return request.Operation == AiOperations.Expand
            ? text + "\n\n補足のサンプルです。手順、比較項目、注意点などを追加する際の表示を確認できます。"
            : text;
    }

    // 通常のPromptBuilderが出力する先頭のラベル行だけを読み、追加指示や本文全体は転載しない。
    private static string? ReadField(string prompt, string label)
    {
        var prefix = label + ": ";
        var line = prompt.Split('\n').FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line?[prefix.Length..].Trim();
    }

    private static int ReadCount(string prompt, string label, int fallback, int min, int max)
    {
        return int.TryParse(ReadField(prompt, label), out var count) ? Math.Clamp(count, min, max) : fallback;
    }
}
