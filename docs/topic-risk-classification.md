# トピックリスク分類設計書

## 1. 目的

本書は、記事テーマ、入力ソース、検索結果、X投稿、ユーザー指定から、記事のリスク区分を`normal`、`strict`、`compliance_strict`へ分類する方針を定義する。

対象は、分類基準、初期辞書、優先順位、TTL連携、プロンプト制約、X再取得、人間確認、WordPress公開可否、辞書更新、テスト観点である。

## 2. 基本方針

- トピックリスク分類はApplication層で行う。
- UI表示制御だけに依存せず、ジョブ登録、本文生成、HTML変換、WordPress投稿前に必要な制約を適用する。
- 分類は安全側に倒し、迷う場合はより厳しい区分を採用する。
- `compliance_strict`はMVPでは`strict + HumanReviewRequired`として扱う。
- 環境、ユーザー、記事、データソース、トピック単位の指定は、より厳しくする方向だけ許可する。
- X投稿を表示または公開利用する場合、production/strictでは公開前再取得を必須にする。
- 分類辞書はYAMLまたはJSONで管理し、更新時はテストケースを追加する。

## 3. リスク区分

| 区分 | 対象 | 主な制約 |
| --- | --- | --- |
| `normal` | 一般SEO記事、ハウツー、技術メモ、ブログ下書き、evergreen記事 | 通常TTL、通常プロンプト、通常レビュー |
| `strict` | 最新情報、ニュース、X投稿利用、料金比較、価格、在庫、ランキング、口コミ、評判、SaaS比較 | 短TTL、出典確認、断定回避、X再取得 |
| `compliance_strict` | 医療、法律、税金、投資、保険、政治、選挙、災害、事件、不祥事、個人や企業の評判毀損 | strict制約、人間確認必須、Publish抑止 |

`strict`は鮮度、価格、評判、外部投稿、比較条件の変化に弱い記事を対象にする。`compliance_strict`は読者の健康、財産、安全、法的判断、社会的評価に重大な影響を与える可能性がある記事を対象にする。

## 4. 入力信号

分類では以下の入力を使う。

| 入力 | 用途 | 注意 |
| --- | --- | --- |
| キーワード | 主題判定 | ユーザー入力として信頼しすぎない |
| タイトル | 記事意図の補助判定 | 生成候補も判定対象にできる |
| タグ、メモ | 補助信号 | 安全制約を弱める根拠にしない |
| 追加プロンプト | ユーザー明示意図の検出 | 「公開してよい」などの指示で制約を緩めない |
| 見出し構成 | セクション単位のリスク検出 | H2/H3ごとに判定できる |
| 検索結果 | 最新性、ニュース性、価格、公式性の検出 | 取得日時とTTLを確認する |
| X投稿 | 口コミ、反応、炎上、評判の検出 | production/strictでは再取得必須 |
| WordPressサイト設定 | サイト別の文体、読者像 | リスク区分を緩める根拠にしない |
| 環境設定 | システム全体の上限 | 最優先の安全装置 |

分類対象テキストにはHTMLを含めない。HTMLやMarkdownが含まれる場合は、テキスト抽出またはエスケープ後の文字列を分類に使う。

## 5. 優先順位

複数のリスク信号がある場合は、より厳しい区分を採用する。

厳しさの順序:

```text
normal < strict < compliance_strict
```

優先順位:

1. 環境単位の強制設定
2. ユーザーまたは組織単位の強制設定
3. 記事単位の明示設定
4. データソース単位の制約
5. トピック辞書による自動判定
6. 入力ソース内容による補助判定

環境単位の設定はシステム全体の上限・安全装置として扱う。ただし、環境単位が`normal`でも、記事やトピックが`strict`または`compliance_strict`に該当する場合は厳しい区分を採用する。

## 6. 分類カテゴリ

初期辞書は以下のカテゴリで管理する。

| カテゴリ | 既定区分 | 目的 |
| --- | --- | --- |
| `freshness` | `strict` | 最新性、変更、発表、終了予定の検出 |
| `newsTrend` | `strict` | ニュース、トレンド、炎上、話題性の検出 |
| `pricing` | `strict` | 価格、料金、課金、キャンペーンの検出 |
| `productAvailability` | `strict` | 在庫、販売状況、発売日、提供地域の検出 |
| `comparisonReview` | `strict` | 比較、レビュー、ランキング、口コミの検出 |
| `techSaaS` | `strict` | API、SDK、AIモデル、制限、新機能、非推奨の検出 |
| `legalFinanceHealth` | `compliance_strict` | 法律、税金、投資、保険、医療、健康の検出 |
| `politicsSafetyReputation` | `compliance_strict` | 政治、災害、事故、事件、不祥事、評判毀損の検出 |
| `sourceSignals` | `strict` | X投稿、SNS反応、外部引用利用の検出 |

`legalFinanceHealth`または`politicsSafetyReputation`に一致した場合は、他カテゴリより優先して`compliance_strict`にする。

## 7. 初期キーワード辞書

初期辞書は以下をベースにする。実装ではJSONまたはYAMLへ分離する。

```yaml
freshness:
  mode: strict
  keywords:
    - 最新
    - 速報
    - 今日
    - 昨日
    - 明日
    - 今週
    - 今月
    - 今年
    - 現在
    - 直近
    - 最近
    - 新着
    - 発表
    - 公開
    - 開始
    - 終了
    - リリース
    - アップデート
    - 仕様変更
    - 変更点
    - 改定
    - 廃止
    - 終了予定
    - 延期

newsTrend:
  mode: strict
  keywords:
    - ニュース
    - トレンド
    - 話題
    - バズ
    - 炎上
    - SNSで話題
    - Xで話題
    - 口コミ
    - 評判
    - 反応
    - 世論
    - 注目
    - 急上昇
    - ランキング
    - 人気

pricing:
  mode: strict
  keywords:
    - 価格
    - 料金
    - 費用
    - 月額
    - 年額
    - 課金
    - 従量課金
    - 無料枠
    - 無料プラン
    - 有料プラン
    - 値上げ
    - 値下げ
    - 割引
    - キャンペーン
    - セール
    - クーポン
    - プラン
    - API料金
    - トークン単価
    - レート制限

productAvailability:
  mode: strict
  keywords:
    - 在庫
    - 入荷
    - 売り切れ
    - 販売中
    - 販売終了
    - 予約開始
    - 予約受付
    - 発売日
    - 納期
    - 配送
    - 出荷
    - 対応状況
    - 提供地域

comparisonReview:
  mode: strict
  keywords:
    - おすすめ
    - 比較
    - 選び方
    - レビュー
    - 口コミ
    - 評判
    - メリット
    - デメリット
    - ランキング
    - 代替
    - 競合
    - 違い
    - どっち
    - 最強
    - ベスト
    - 人気順

techSaaS:
  mode: strict
  keywords:
    - API
    - SDK
    - モデル
    - AIモデル
    - LLM
    - プロバイダー
    - 料金比較
    - 制限
    - 上限
    - コンテキスト長
    - トークン
    - レートリミット
    - 新機能
    - 廃止予定
    - 非推奨
    - ベータ
    - Preview
    - プレビュー
    - GA

legalFinanceHealth:
  mode: compliance_strict
  keywords:
    - 法律
    - 規制
    - 違法
    - 合法
    - 契約
    - 著作権
    - 税金
    - 確定申告
    - 投資
    - 株
    - 仮想通貨
    - 暗号資産
    - 保険
    - ローン
    - 金利
    - 病気
    - 症状
    - 治療
    - 薬
    - 副作用
    - 診断
    - 健康
    - 医療

politicsSafetyReputation:
  mode: compliance_strict
  keywords:
    - 政治
    - 選挙
    - 政党
    - 首相
    - 大統領
    - 議員
    - 政策
    - 災害
    - 地震
    - 台風
    - 津波
    - 事故
    - 事件
    - 逮捕
    - 疑惑
    - 告発
    - 不祥事
    - 詐欺
    - ハラスメント

sourceSignals:
  mode: strict
  keywords:
    - X投稿
    - ツイート
    - ポスト
    - 引用
    - 埋め込み
    - SNS投稿
    - ユーザーの反応
    - SNSの反応
    - コメント
```

辞書のキーワードは完全一致だけに依存しない。日本語の表記揺れ、英字大小、全角半角、複合語を正規化してから判定する。

## 8. 除外語と緩和条件

除外語は誤判定を減らすために使う。ただし、除外語で`compliance_strict`を`normal`へ緩めない。

| 用途 | 例 | 方針 |
| --- | --- | --- |
| 技術用語の除外 | `health check`、`policy pattern` | 文脈が技術用語なら`normal`または`strict`へ留める |
| 慣用表現の除外 | `炎上しない設計` | 実在人物や企業の評判でなければ`strict`へ留める |
| 架空作品の除外 | `選挙を題材にした小説` | 現実の政治判断でなければ`strict`へ留める |
| 過去固定情報 | `歴史上の事件の概要` | 最新性や名誉毀損リスクが低ければ`strict`へ留める |

緩和は安全側に制限する。自動判定で`compliance_strict`になった記事を`normal`へ下げる場合は、人間確認を必要とする。

## 9. 分類アルゴリズム

分類処理は以下の順で行う。

1. 入力テキストを集約する。
2. HTML、Markdown記号、制御文字を除去または正規化する。
3. 全角半角、英字大小、空白、記号を正規化する。
4. 環境、ユーザー、記事、データソースの明示設定を読み込む。
5. トピック辞書のカテゴリ一致を判定する。
6. X投稿利用、検索利用、価格・最新性などの補助信号を判定する。
7. 最も厳しい区分を採用する。
8. `reasons`、`matchedCategories`、`matchedKeywords`、`effectivePolicy`を返す。

疑似コード:

```text
policy = normal

policy = max(policy, environmentPolicy)
policy = max(policy, userPolicy)
policy = max(policy, articlePolicy)
policy = max(policy, dataSourcePolicy)

for category in dictionary:
  if matches(category, normalizedInput):
    policy = max(policy, category.mode)

if usesXPost:
  policy = max(policy, strict)

if policy == compliance_strict:
  humanReviewRequired = true

return policy
```

`max`は`normal < strict < compliance_strict`の順序で比較する。

## 10. 区分ごとの挙動

| 処理 | `normal` | `strict` | `compliance_strict` |
| --- | --- | --- | --- |
| プロンプト | 通常品質制約 | 断定回避、出典確認、取得日重視 | 専門的助言回避、人間確認前提 |
| Tavily TTL | 通常TTL | strict TTL | strict TTL |
| X生データTTL | 通常TTL | strict TTL | strict TTL |
| X表示前再取得 | 環境に従う | production/strictで必須 | 必須 |
| WordPress下書き | 可能 | 可能 | 可能 |
| WordPress公開 | 可能 | 条件付き可能 | 人間確認前は不可 |
| 品質チェック | 通常 | 最新性、出典、口コミ根拠を追加確認 | YMYL、名誉毀損、法的・医療的断定を確認 |
| ログ | 通常 | 判定理由と再取得要否を記録 | HumanReviewRequiredを記録 |

## 11. TTL連携

TTLは[データ保持・プライバシー設計書](data-retention-privacy.md)の決定ルールに従う。

TTL候補:

1. 環境単位TTL
2. ユーザー単位TTL
3. 記事単位TTL
4. データソース単位TTL
5. トピック単位TTL

最終TTLは最も短い値を採用する。環境単位TTLより長くする上書きは無効とする。

環境別の基準:

| 環境 | Tavily検索結果JSON | Tavily本文・要約・スニペット | X投稿生データ | X表示・公開前 |
| --- | --- | --- | --- | --- |
| `dev` | 24時間 | 24時間 | 6時間 | 任意 |
| `staging` | 6時間 | 24時間 | 6時間 | 推奨 |
| `production` | 24時間 | 7日 | 24時間 | 必須 |
| `strict` | 24時間 | 24時間 | 1時間 | 必須 |

`compliance_strict`はTTLとしては`strict`を適用し、さらに`HumanReviewRequired`を付与する。

## 12. プロンプト連携

分類結果は[プロンプト設計書](prompt-design.md)の`SafetyBlock`へ反映する。

| 区分 | 追加プロンプト制約 |
| --- | --- |
| `normal` | 通常のSEO記事として、検索意図、独自情報、読みやすさを重視する |
| `strict` | 最新性、価格、仕様、ランキング、口コミ、SaaS比較の断定を避ける。取得日、出典、前提条件を明示する |
| `compliance_strict` | 医療、法律、税金、投資、保険、政治、安全、事件、評判に関する助言や断定を避ける。人間確認前提で出力する |

AIへは判定理由を短く渡す。辞書全文、内部判定ロジック、秘密情報、ユーザーIDは渡さない。

## 13. 品質チェック連携

分類結果は[記事品質ガイドライン](article-quality-guidelines.md)の品質判定に反映する。

| 区分 | 品質チェック |
| --- | --- |
| `normal` | タイトル、本文、見出し、出典不足の断定を確認する |
| `strict` | 最新性、取得日、一次情報、口コミ根拠、価格・在庫の変動を確認する |
| `compliance_strict` | YMYL、名誉毀損、専門的助言、政治的誘導、人間確認状態を確認する |

`compliance_strict`で著者、監修者、出典、更新日、注意事項のいずれかが不足する場合、公開前に`HumanReviewRequired`を維持する。

## 14. X投稿連携

X投稿を使う記事は、最低でも`strict`として扱う。

X投稿利用に該当する条件:

- X API Full-Archive Searchを実行する。
- X投稿本文、投稿者名、投稿日時、投稿URLを本文または引用元カードへ使う。
- 検索結果や追加プロンプトにX投稿URLが含まれる。
- ユーザーがX投稿引用、SNS反応、口コミ、炎上、評判を指定する。

production/strictでは、X投稿の表示またはWordPress投稿前に再取得する。削除、非公開、編集、取得不能の場合は引用を停止し、必要に応じて`HumanReviewRequired`へ上げる。

## 15. WordPress公開制御

WordPress投稿では以下を適用する。

| 条件 | Draft | Publish |
| --- | --- | --- |
| `normal` | 可能 | 可能 |
| `strict` | 可能 | X再取得、出典、最新性確認後に可能 |
| `compliance_strict` | 可能 | 人間確認前は不可 |
| `HumanReviewRequired = true` | 可能 | 人間確認前は不可 |
| X再取得未完了 | 可能 | 不可 |
| X再取得失敗 | 引用停止後なら可能 | 人間確認が必要 |

投稿ステータス未指定時は`Draft`とする。自動投稿を有効にする場合も、公開投稿は明示操作または人間確認済み状態を必要とする。

## 16. 判定例

| 入力 | 判定 | 理由 |
| --- | --- | --- |
| `Blazor Web Appのフォーム実装方法` | `normal` | 技術ハウツーで最新性やYMYL信号が弱い |
| `Gemini API料金の最新比較` | `strict` | API料金、最新、比較に一致 |
| `Xで話題のSaaS口コミまとめ` | `strict` | X投稿、口コミ、SaaS比較に一致 |
| `WordPressプラグインおすすめランキング` | `strict` | おすすめ、ランキング、比較に一致 |
| `副作用のある薬の選び方` | `compliance_strict` | 医療、薬、副作用に一致 |
| `暗号資産の投資判断` | `compliance_strict` | 投資、暗号資産に一致 |
| `企業不祥事の評判まとめ` | `compliance_strict` | 不祥事、評判、名誉毀損リスクに一致 |
| `選挙制度の基礎解説` | `compliance_strict` | 政治、選挙に一致 |
| `2026年版SEOライティングの正解` | `strict` | 最新性、SEO比較、AI検索の変動性に一致 |

## 17. 判定結果のデータ形

MVPでは専用Entityを追加しない。判定結果はジョブPayload、記事状態、品質チェック結果、ログで扱う。

想定DTO:

```json
{
  "topicRisk": "strict",
  "humanReviewRequired": false,
  "matchedCategories": ["freshness", "pricing"],
  "matchedKeywords": ["最新", "料金"],
  "requiresXRehydration": false,
  "ttlPolicy": "strict",
  "reasons": [
    "最新性が必要なキーワードに一致",
    "料金比較に一致"
  ]
}
```

`matchedKeywords`は利用者に表示できる範囲に限定する。入力本文全文、プロンプト全文、X投稿本文全文は保存しない。

## 18. 設定管理

分類辞書は設定ファイルまたはDB管理を検討する。

MVPの推奨:

| 項目 | 方針 |
| --- | --- |
| 管理形式 | JSONまたはYAML |
| 配置 | アプリ設定配下またはInfrastructure層の静的設定 |
| 更新担当 | 運営者本人 |
| 更新頻度 | 月1回、または誤判定に気づいた時 |
| レビュー | 変更差分と判定テストを確認する |
| ロールバック | 前バージョンへ戻せるようGit管理する |

本番運用で非エンジニアが辞書を更新する必要が出るまでは、DB管理画面は作らない。

## 19. エラー処理

分類専用ErrorCodeはMVPでは追加しない。既存コードを使う。

| 状況 | ErrorCode | 方針 |
| --- | --- | --- |
| 入力が空 | `ValidationError` | `normal`へ倒さず、記事作成側の必須入力として拒否する |
| 辞書ファイルが壊れている | `ConfigurationError` | 起動時またはヘルスチェックで検出する |
| 判定処理失敗 | `UnknownError` | 安全側に`strict`として扱い、ログへtraceIdを残す |
| 人間確認が必要 | `HumanReviewRequired` | Publishを拒否する |
| X再取得が必要 | `XRehydrationRequired` | 表示または投稿前に再取得を要求する |
| X再取得失敗 | `XRehydrationFailed` | 引用停止または人間確認へ回す |

`ConfigurationError`が既存ErrorCodeにない場合は、実装時に追加可否を[エラーコードリファレンス](error-codes.md)で判断する。

## 20. ログ・監査

ログへ出してよい情報:

- `traceId`
- `userId`
- `articleId`
- `jobId`
- `topicRisk`
- `matchedCategories`
- `reasonCode`
- `humanReviewRequired`
- `requiresXRehydration`

ログへ出さない情報:

- 記事本文全文
- プロンプト全文
- X投稿本文全文
- 外部APIレスポンス全文
- 秘密情報、認証情報、Webhook URL

監査ログには、人間確認完了、Publish拒否、リスク区分の手動変更などの操作結果を記録する。

## 21. テスト観点

| ID | 観点 | 期待結果 |
| --- | --- | --- |
| `TRC-001` | normal判定 | 一般ハウツーが`normal`になる |
| `TRC-002` | freshness判定 | 最新、今日、変更点などが`strict`になる |
| `TRC-003` | pricing判定 | 価格、料金、API料金などが`strict`になる |
| `TRC-004` | X投稿判定 | X投稿利用が`strict`になる |
| `TRC-005` | legalFinanceHealth判定 | 医療、法律、投資が`compliance_strict`になる |
| `TRC-006` | politicsSafetyReputation判定 | 政治、事件、不祥事が`compliance_strict`になる |
| `TRC-007` | 複数一致 | strictとcompliance_strictが混在した場合に`compliance_strict`になる |
| `TRC-008` | TTL優先 | 複数TTL候補のうち最短TTLが採用される |
| `TRC-009` | X再取得 | production/strictでX表示前再取得が要求される |
| `TRC-010` | Publish抑止 | `HumanReviewRequired`でPublishが拒否される |
| `TRC-011` | 除外語 | 技術用語のhealth checkが医療扱いされない |
| `TRC-012` | 辞書更新 | 辞書変更時に判定テストが更新される |
| `TRC-013` | ログ除外 | 本文全文、プロンプト全文、X投稿本文全文がログに出ない |

テストではGemini、Tavily、X API、WordPressの実APIを呼ばない。

## 22. 実装順序

1. `TopicRisk`列挙型を定義する。
2. 分類辞書の設定形式を定義する。
3. 入力正規化処理を実装する。
4. カテゴリ一致判定を実装する。
5. 環境、ユーザー、記事、データソース、トピック単位の優先順位を実装する。
6. 判定結果DTOを定義する。
7. プロンプトBuilderへ分類結果を渡す。
8. データ保持TTL決定へ分類結果を渡す。
9. WordPress投稿前チェックへ`HumanReviewRequired`とX再取得要否を組み込む。
10. `TRC-*`テストを追加する。

## 23. 決定事項

- トピック自動判定は`normal`、`strict`、`compliance_strict`の3区分とする。
- `compliance_strict`はMVPでは`strict + HumanReviewRequired`として扱う。
- X投稿を使う記事は最低でも`strict`とする。
- 環境、ユーザー、記事、データソース、トピック単位の指定は、より厳しくする方向だけ許可する。
- 辞書はYAMLまたはJSONで管理し、更新時はテストケースを追加する。
- 自動判定で迷う場合は安全側に倒す。

## 24. 関連ドキュメント

- [要件定義書](requirements.md)
- [外部連携設計書](external-integration-design.md)
- [データ保持・プライバシー設計書](data-retention-privacy.md)
- [プロンプト設計書](prompt-design.md)
- [記事品質ガイドライン](article-quality-guidelines.md)
- [コンテンツ更新・メンテナンス設計書](content-update-maintenance.md)
- [コンテンツレンダリング設計書](content-rendering-design.md)
- [エラーコードリファレンス](error-codes.md)
- [設定リファレンス](configuration-reference.md)
- [テスト設計書](test-design.md)
