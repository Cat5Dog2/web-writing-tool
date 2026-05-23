using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebWritingTool.Application.Search;

public enum TopicRiskMode
{
    Normal,
    Strict,
    ComplianceStrict
}

public sealed record TopicRiskClassification(
    TopicRiskMode Mode,
    string? MatchedCategory,
    string? MatchedKeyword,
    bool HumanReviewRequired);

public interface ITopicRiskClassifier
{
    TopicRiskClassification Classify(params string?[] inputs);
}

public sealed class TopicRiskClassifier(TopicRiskKeywordDictionary dictionary) : ITopicRiskClassifier
{
    public TopicRiskClassification Classify(params string?[] inputs)
    {
        var text = string.Join(
            "\n",
            inputs
                .Where(input => !string.IsNullOrWhiteSpace(input))
                .Select(input => input!.Trim()));

        if (string.IsNullOrWhiteSpace(text))
        {
            return Normal();
        }

        var compliance = MatchAny(text, dictionary.ComplianceStrictCategories);
        if (compliance is not null)
        {
            return new TopicRiskClassification(
                TopicRiskMode.ComplianceStrict,
                compliance.Value.Category,
                compliance.Value.Keyword,
                HumanReviewRequired: true);
        }

        var strict = MatchAny(text, dictionary.StrictCategories);
        return strict is null
            ? Normal()
            : new TopicRiskClassification(
                TopicRiskMode.Strict,
                strict.Value.Category,
                strict.Value.Keyword,
                HumanReviewRequired: false);
    }

    private static TopicRiskClassification Normal()
    {
        return new TopicRiskClassification(TopicRiskMode.Normal, null, null, false);
    }

    private static (string Category, string Keyword)? MatchAny(
        string text,
        IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
    {
        foreach (var (category, keywords) in categories)
        {
            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword)
                    && text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return (category, keyword);
                }
            }
        }

        return null;
    }
}

public sealed record TopicRiskKeywordDictionary(
    IReadOnlyDictionary<string, IReadOnlyList<string>> StrictCategories,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ComplianceStrictCategories)
{
    public static TopicRiskKeywordDictionary Default { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["freshness"] =
            [
                "最新", "速報", "今日", "昨日", "明日", "今週", "今月", "今年", "現在", "直近", "最近",
                "新着", "発表", "公開", "開始", "終了", "リリース", "アップデート", "仕様変更",
                "変更点", "改定", "廃止", "終了予定", "延期"
            ],
            ["newsTrend"] =
            [
                "ニュース", "トレンド", "話題", "バズ", "炎上", "SNSで話題", "Xで話題", "口コミ",
                "評判", "反応", "世論", "注目", "急上昇", "ランキング", "人気"
            ],
            ["pricing"] =
            [
                "価格", "料金", "費用", "月額", "年額", "課金", "従量課金", "無料枠", "無料プラン",
                "有料プラン", "値上げ", "値下げ", "割引", "キャンペーン", "セール", "クーポン",
                "プラン", "API料金", "トークン単価", "レート制限"
            ],
            ["productAvailability"] =
            [
                "在庫", "入荷", "売り切れ", "販売中", "販売終了", "予約開始", "予約受付",
                "発売日", "納期", "配送", "出荷", "対応状況", "提供地域"
            ],
            ["comparisonReview"] =
            [
                "おすすめ", "比較", "選び方", "レビュー", "口コミ", "評判", "メリット", "デメリット",
                "ランキング", "代替", "競合", "違い", "どっち", "最強", "ベスト", "人気順"
            ],
            ["techSaaS"] =
            [
                "API", "SDK", "モデル", "AIモデル", "LLM", "プロバイダー", "料金比較", "制限",
                "上限", "コンテキスト長", "トークン", "レートリミット", "新機能", "廃止予定",
                "非推奨", "ベータ", "Preview", "プレビュー", "GA"
            ],
            ["sourceSignals"] =
            [
                "X投稿", "ツイート", "ポスト", "引用", "埋め込み", "SNS投稿", "ユーザーの反応",
                "SNSの反応", "コメント"
            ]
        },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["legalFinanceHealth"] =
            [
                "法律", "規制", "違法", "合法", "契約", "著作権", "税金", "確定申告", "投資",
                "株", "仮想通貨", "暗号資産", "保険", "ローン", "金利", "病気", "症状",
                "治療", "薬", "副作用", "診断", "健康", "医療"
            ],
            ["politicsSafetyReputation"] =
            [
                "政治", "選挙", "政党", "首相", "大統領", "議員", "政策", "災害", "地震",
                "台風", "津波", "事故", "事件", "逮捕", "疑惑", "告発", "不祥事", "詐欺",
                "ハラスメント"
            ]
        });
}

public static class TopicRiskKeywordDictionaryLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static TopicRiskKeywordDictionary LoadJson(Stream stream)
    {
        var document = JsonSerializer.Deserialize<TopicRiskKeywordJson>(stream, JsonOptions)
            ?? throw new JsonException("Topic risk dictionary JSON is empty.");

        return new TopicRiskKeywordDictionary(
            ToDictionary(document.Strict),
            ToDictionary(document.ComplianceStrict));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ToDictionary(
        Dictionary<string, string[]>? source)
    {
        return (source ?? [])
            .ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .Select(keyword => keyword.Trim())
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private sealed record TopicRiskKeywordJson(
        Dictionary<string, string[]>? Strict,
        Dictionary<string, string[]>? ComplianceStrict);
}
