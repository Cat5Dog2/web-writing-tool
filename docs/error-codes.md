# エラーコードリファレンス

## 1. 目的

本書は、AIライティングツールで使用するエラーコード、HTTPステータス、ProblemDetails、ジョブ失敗理由、ログ出力、画面表示の方針を定義する。

対象は、Minimal API、Applicationサービス、BackgroundService、外部連携Client、Blazor UI、監査ログ、運用ログである。

## 2. 基本方針

- エラーコードは機械判定用の安定した識別子とする。
- APIエラー応答はProblemDetailsを基本形とし、拡張フィールドとして`errorCode`を含める。
- ジョブ、WordPress投稿履歴、通知ログには`ErrorCode`と短い`ErrorMessage`を保存する。
- `ErrorMessage`は画面表示できる安全な概要のみとする。
- 利用者に表示する`detail`、フィールド別`errors`、`ErrorMessage`、画面表示文言は「です・ます」調とする。
- APIキー、Bearer Token、Cookie、CSRF Token、WordPress Application Password、Discord Webhook URL、プロンプト全文、記事本文全文、外部APIレスポンス全文をエラーへ含めない。
- 外部API固有のエラーはInfrastructure層で共通エラーコードへ正規化する。
- 再試行可否は`ErrorCode`とジョブ試行回数に基づいて決定する。
- 新しいエラーコードを追加する場合は、本書、API設計、ジョブ設計、テスト設計を同時に更新する。

## 3. 命名規則

| 項目 | 方針 |
| --- | --- |
| 形式 | PascalCase |
| 値の例 | `ValidationError`, `UnauthorizedExternalApi`, `RunningJobExists` |
| 禁止 | HTTPステータス番号を含める命名、外部サービスの生エラー名をそのまま使う命名 |
| 粒度 | UIの分岐、再試行可否、運用対応が変わる単位で分ける |
| 互換性 | 一度公開したコードは意味を変えない |

Provider名を含むコードは、画面表示や運用対応がProvider固有になる場合だけ使用する。HTTP 429のように再試行方針が共通のものは`RateLimited`を使う。

## 4. ProblemDetails

APIエラー応答は以下を基本形とする。

```json
{
  "type": "https://web-writing-tool.local/problems/validation-error",
  "title": "Validation error",
  "status": 400,
  "detail": "入力内容に不備があります。",
  "instance": "/api/articles",
  "traceId": "00-...",
  "errorCode": "ValidationError",
  "errors": {
    "keyword": [
      "キーワードは必須です。"
    ]
  }
}
```

| プロパティ | 必須 | 説明 |
| --- | --- | --- |
| `type` | 必須 | 問題種別URL。`/problems/{kebab-case-error-code}`を基本とする |
| `title` | 必須 | 英語の短い分類名 |
| `status` | 必須 | HTTPステータス |
| `detail` | 任意 | 利用者向けの安全な概要 |
| `instance` | 任意 | APIパス |
| `traceId` | 必須 | 調査用トレースID |
| `errorCode` | 必須 | 本書で定義するErrorCode |
| `errors` | 条件付き | 入力検証エラーのフィールド別詳細 |

`detail`と`errors`に入れる日本語メッセージは「です・ます」調で統一する。英語の`title`は機械的な分類名として扱い、文体統一の対象外とする。

## 5. HTTPステータス対応

| HTTPステータス | 主なErrorCode | 用途 |
| --- | --- | --- |
| `400 Bad Request` | `ValidationError`, `CurrentPasswordMismatch`, `ConfirmTextMismatch`, `SelfDeleteForbidden`, `LastAdminCannotBeDeleted`, `LastAdminCannotBeDemoted` | リクエスト形式、入力値、明示的な操作条件の不備 |
| `401 Unauthorized` | `Unauthorized` | 未認証 |
| `403 Forbidden` | `Forbidden` | 認可不足、所有者不一致 |
| `404 Not Found` | `NotFound` | 対象なし。権限隠蔽を含む |
| `409 Conflict` | `Conflict`, `RunningJobExists`, `UserHasRunningJobs`, `JobNotCancelable`, `JobNotRetryable` | 状態競合、実行中ジョブ、同時更新 |
| `422 Unprocessable Entity` | `UsageLimitExceeded`, `ArticleNotPostable`, `HumanReviewRequired`, `XRehydrationRequired`, `XRehydrationFailed` | 形式は正しいが業務ルール上実行できない |
| `429 Too Many Requests` | `RateLimited` | アプリ側または外部連携先のレート制限 |
| `500 Internal Server Error` | `UnknownError` | 予期しないサーバーエラー |

所有者不一致は`403`または`404`を使う。対象リソースの存在を隠す必要があるAPIでは`404 Not Found`を優先する。

## 6. 共通エラー

| ErrorCode | HTTP | 再試行 | 説明 |
| --- | --- | --- | --- |
| `ValidationError` | 400 | 不可 | 入力値、形式、列挙値、URL、文字数の検証エラー |
| `Unauthorized` | 401 | 不可 | 未認証 |
| `Forbidden` | 403 | 不可 | 権限不足 |
| `NotFound` | 404 | 不可 | 対象リソースなし |
| `Conflict` | 409 | 不可 | RowVersion不一致などの一般的な状態競合 |
| `RateLimited` | 429 | 可 | レート制限。`Retry-After`があれば優先する |
| `Timeout` | 500またはジョブ失敗 | 可 | 外部APIまたは内部処理のタイムアウト |
| `UnknownError` | 500 | 条件付き | 予期しない例外。最大試行回数まで再試行可 |

`Timeout`は同期APIの利用者向け応答では`UnknownError`として隠蔽してよい。ジョブ保存時は再試行制御のため`Timeout`を保存する。

## 7. 認証・認可エラー

| ErrorCode | HTTP | 説明 |
| --- | --- | --- |
| `Unauthorized` | 401 | ログインしていない |
| `Forbidden` | 403 | ロール不足、所有者不一致、無効ユーザー |
| `SelfDeleteForbidden` | 400 | 管理者APIで自分自身を削除しようとした |
| `LastAdminCannotBeDeleted` | 400 | 最後のAdminユーザーを削除しようとした |
| `LastAdminCannotBeDemoted` | 400 | 最後のAdminユーザーを降格または無効化しようとした |
| `CurrentPasswordMismatch` | 400 | 本人退会時の現在パスワード不一致 |
| `ConfirmTextMismatch` | 400 | 本人退会時の確認文字列不一致 |
| `UserHasRunningJobs` | 409 | 削除対象ユーザーに`Running`ジョブが存在する |

パスワード不一致では、入力されたパスワードやハッシュをログ、監査ログ、レスポンスへ出さない。

## 8. 入力検証エラー

| ErrorCode | HTTP | 対象 |
| --- | --- | --- |
| `ValidationError` | 400 | 必須、文字数、配列件数、列挙値、URL形式、Discord Webhook形式 |
| `InvalidUrl` | 400 | HTTPS以外、localhost、private IP、link-local、metadata IP |
| `InvalidParentHeading` | 400または422 | H3の親が同一記事内のH2ではない |
| `GenerateImageNotSupported` | 400 | MVPで`generateImage = true`が指定された |

URL検証ではSSRF対策を優先し、DNS解決後のIPも検証する。

## 9. 記事・見出しエラー

| ErrorCode | HTTP | 説明 |
| --- | --- | --- |
| `ArticleNotPostable` | 422 | 記事ステータスが`Completed`または`Posted`ではない |
| `ArticleHasRunningJob` | 409 | 記事に`Running`ジョブがあるため削除できない |
| `HeadingHasRunningJob` | 409 | 見出しに`Running`ジョブがあるため変更できない |
| `RunningJobExists` | 409 | 同一対象に`Queued`または`Running`ジョブがある |
| `HumanReviewRequired` | 422 | `compliance_strict`または人間確認必須の記事を公開しようとした |
| `XRehydrationRequired` | 422 | productionまたはstrictでX投稿の再取得が未完了 |
| `XRehydrationFailed` | 422 | X投稿の再取得で削除、非公開、編集、取得不能を検出した |
| `SearchCacheExpired` | 422 | 必要な検索キャッシュがTTL満了で使用できない |

WordPress下書き投稿は`HumanReviewRequired`の対象外とする。公開投稿だけ抑止する。

## 10. ジョブエラー

| ErrorCode | 再試行 | 説明 |
| --- | --- | --- |
| `ValidationError` | 不可 | Payload不正、処理前検証エラー |
| `UnauthorizedExternalApi` | 不可 | 外部API認証失敗 |
| `ForbiddenExternalApi` | 不可 | 外部API権限不足 |
| `RateLimited` | 可 | HTTP 429、利用頻度制限 |
| `Timeout` | 可 | 外部APIまたは処理タイムアウト |
| `ExternalServerError` | 可 | 外部API 5xx |
| `ExternalBadResponse` | 条件付き | JSONパース失敗、想定外レスポンス |
| `NetworkError` | 可 | DNS、接続、TLSなどの一時障害 |
| `UsageLimitExceeded` | 不可 | アプリ側利用上限超過 |
| `NotFound` | 不可 | 対象記事、見出し、サイト、通知設定が存在しない |
| `Conflict` | 不可 | 状態不整合 |
| `UnknownError` | 条件付き | 未分類例外。最大試行回数まで |

`ArticleGenerationJobs.ErrorCode`には上記の値を保存する。`ErrorMessage`には利用者に表示できる「です・ます」調の短い概要だけを保存する。

## 11. 外部APIエラー

| ErrorCode | 再試行 | 説明 |
| --- | --- | --- |
| `UnauthorizedExternalApi` | 不可 | APIキー、Bearer Token、Application Passwordが無効 |
| `ForbiddenExternalApi` | 不可 | 契約、権限、スコープ不足 |
| `RateLimited` | 可 | HTTP 429 |
| `Timeout` | 可 | タイムアウト |
| `ExternalServerError` | 可 | HTTP 5xx |
| `ExternalBadResponse` | 条件付き | 壊れたJSON、必須項目欠落、想定外形式 |
| `NetworkError` | 可 | DNS、接続、TLS障害 |
| `UnknownExternalError` | 条件付き | 未分類の外部連携エラー |

ログには`provider`、`operation`、`statusCode`、`errorCode`、`jobId`、`articleId`、`elapsedMs`を記録する。認証ヘッダー、Cookie、APIキー、外部APIレスポンス全文は記録しない。

## 12. Provider別エラー

Provider別コードは、履歴や画面上でProvider固有の案内が必要な場合に使う。

| ErrorCode | 主な保存先 | 説明 |
| --- | --- | --- |
| `GeminiGenerationFailed` | `ArticleGenerationJobs`, `AiGenerationLogs` | Geminiテキスト生成失敗 |
| `TavilySearchFailed` | `ArticleGenerationJobs`, `SearchResults`関連ログ | Tavily検索失敗 |
| `XSearchFailed` | `ArticleGenerationJobs`, `XSearchPosts`関連ログ | X Full-Archive Search失敗 |
| `XRehydrationFailed` | `ArticleGenerationJobs`, `WordpressPosts` | X投稿再取得失敗 |
| `WordpressConnectionFailed` | API応答、監査ログ | WordPress接続テスト失敗 |
| `WordpressCategoryFetchFailed` | API応答、運用ログ | WordPressカテゴリ取得失敗 |
| `WordpressPostFailed` | `WordpressPosts`, `ArticleGenerationJobs` | WordPress投稿失敗 |
| `DiscordSendFailed` | `NotificationLogs`, `ArticleGenerationJobs` | Discord通知送信失敗 |

Provider別コードと共通コードを両方保存できる場合は、機械判定用に共通コードを`ErrorCode`へ保存し、Provider別の分類は`provider`または`operation`で表す。Provider別コードを`ErrorCode`へ保存するのは、共通コードだけでは画面表示や復旧手順を判定できない場合に限定する。

## 13. WordPressエラー

| ErrorCode | HTTPまたは保存先 | 説明 |
| --- | --- | --- |
| `WordpressConnectionFailed` | 接続テストAPI | URL、認証、REST API疎通の失敗 |
| `WordpressCategoryFetchFailed` | カテゴリ取得API | カテゴリ一覧取得失敗 |
| `WordpressPostFailed` | `WordpressPosts.ErrorCode` | 投稿作成失敗 |
| `UnauthorizedExternalApi` | ジョブ、履歴 | Application Password不正 |
| `ForbiddenExternalApi` | ジョブ、履歴 | 投稿権限不足 |
| `ExternalBadResponse` | ジョブ、履歴 | WordPress REST APIの想定外レスポンス |

WordPress接続テストの業務上の失敗は`200 OK`かつ`success: false`で返す。API自体が処理不能な場合だけ5xxとする。

## 14. Discord通知エラー

| ErrorCode | 保存先 | 説明 |
| --- | --- | --- |
| `DiscordSendFailed` | `NotificationLogs.ErrorCode` | Discord Webhook送信失敗 |
| `UnauthorizedExternalApi` | `NotificationLogs.ErrorCode` | Webhook URL無効または認証相当の失敗 |
| `RateLimited` | `NotificationLogs.ErrorCode` | Discord側レート制限 |
| `Timeout` | `NotificationLogs.ErrorCode` | 送信タイムアウト |

通知本文と通知ログにはWebhook URL、記事本文全文、プロンプト全文を含めない。

## 15. 検索・X APIエラー

| ErrorCode | HTTPまたは保存先 | 説明 |
| --- | --- | --- |
| `TavilySearchFailed` | ジョブ、運用ログ | Tavily検索失敗 |
| `XSearchFailed` | ジョブ、運用ログ | X Full-Archive Search失敗 |
| `XMonthlySafetyLimitExceeded` | 422またはジョブ失敗 | X検索の月間安全上限超過見込み |
| `XRehydrationRequired` | 422 | X投稿の再取得が必要 |
| `XRehydrationFailed` | 422またはジョブ失敗 | 再取得で削除、非公開、編集、取得不能を検出 |
| `SearchCacheExpired` | 422またはジョブ失敗 | TTL満了により検索キャッシュを利用できない |

productionとstrictでは、X投稿の表示またはWordPress投稿前に再hydrationを必須とする。

## 16. データ保持・削除エラー

| ErrorCode | HTTP | 説明 |
| --- | --- | --- |
| `UserHasRunningJobs` | 409 | ユーザー物理削除前に`Running`ジョブが存在する |
| `LastAdminCannotBeDeleted` | 400 | 最後のAdminユーザー削除 |
| `SelfDeleteForbidden` | 400 | 管理者APIで自分自身を削除 |
| `DataRetentionPolicyViolation` | 500または運用ログ | TTL満了後のデータが削除またはNULL化されていない |
| `CacheCleanupFailed` | 運用ログ / Worker失敗 | 期限切れ検索キャッシュ削除Workerの失敗 |

削除処理の失敗ログには削除件数、対象テーブル、`traceId`を記録する。削除対象本文や秘密情報は記録しない。

## 17. 保存先

| 保存先 | 項目 | 方針 |
| --- | --- | --- |
| APIレスポンス | `errorCode`, `traceId` | ProblemDetails拡張フィールド |
| `ArticleGenerationJobs` | `ErrorCode`, `ErrorMessage` | ジョブ失敗理由と再試行判定 |
| `WordpressPosts` | `ErrorCode`, `ErrorMessage` | 投稿失敗履歴 |
| `NotificationLogs` | `ErrorCode`, `ErrorMessage` | 通知失敗履歴 |
| `AiGenerationLogs` | `ErrorCode` | 生成失敗概要。プロンプト全文は保存しない |
| 運用ログ | `errorCode`, `traceId`, `jobId`, `provider` | 構造化ログ |
| 監査ログ | 操作結果、短い概要 | 秘密情報と本文全文を含めない |

## 18. 画面表示方針

| 対象 | 表示内容 |
| --- | --- |
| 一般ユーザー | 安全な概要、再実行可否、設定確認先 |
| 管理者 | `errorCode`、`traceId`、`jobId`、Provider、発生時刻 |
| ジョブ詳細 | `ErrorCode`、短い`ErrorMessage`、再実行ボタンの可否 |
| WordPress履歴 | 投稿先サイト名、投稿ステータス、`ErrorCode`、短い概要 |
| 通知履歴 | Provider、イベント種別、送信結果、`ErrorCode` |

スタックトレース、内部例外、外部APIレスポンス全文、秘密情報は本番画面に表示しない。

ユーザー向けメッセージ例:

| ErrorCode | 表示メッセージ |
| --- | --- |
| `ValidationError` | 入力内容に不備があります。 |
| `Unauthorized` | ログインが必要です。 |
| `Forbidden` | この操作を実行する権限がありません。 |
| `NotFound` | 対象のデータが見つかりません。 |
| `RunningJobExists` | 対象の処理が実行中です。完了後に再度操作してください。 |
| `UsageLimitExceeded` | 利用上限に達しています。 |
| `RateLimited` | リクエストが集中しています。時間をおいて再実行してください。 |
| `Timeout` | 処理がタイムアウトしました。時間をおいて再実行してください。 |
| `WordpressConnectionFailed` | WordPress接続に失敗しました。設定を確認してください。 |
| `WordpressPostFailed` | WordPress投稿に失敗しました。 |
| `DiscordSendFailed` | 通知送信に失敗しました。Webhook設定を確認してください。 |
| `XRehydrationFailed` | X投稿の再取得に失敗しました。引用内容を確認してください。 |
| `HumanReviewRequired` | 人間確認が完了するまで公開投稿できません。 |
| `UnknownError` | 予期しないエラーが発生しました。時間をおいて再実行してください。 |

## 19. テスト観点

| ID | 観点 | 期待結果 |
| --- | --- | --- |
| `ERR-001` | 入力不正 | `400`、`ValidationError`、フィールド別`errors` |
| `ERR-002` | 未認証 | `401`、`Unauthorized` |
| `ERR-003` | 権限不足 | `403`または`404`、秘密情報なし |
| `ERR-004` | Runningジョブあり記事削除 | `409`、`ArticleHasRunningJob`または`RunningJobExists` |
| `ERR-005` | 利用上限超過 | `422`、`UsageLimitExceeded` |
| `ERR-006` | 外部API 429 | `RateLimited`へ変換し、再試行対象 |
| `ERR-007` | 外部API 401 | `UnauthorizedExternalApi`へ変換し、再試行不可 |
| `ERR-008` | 壊れたJSON | `ExternalBadResponse`へ変換 |
| `ERR-009` | WordPress投稿失敗 | `WordpressPosts.ErrorCode`へ保存し、記事本文を失わない |
| `ERR-010` | Discord通知失敗 | `NotificationLogs.ErrorCode`へ保存し、元ジョブ結果を変更しない |
| `ERR-011` | X再hydration失敗 | 公開投稿を抑止し、`XRehydrationFailed`を返す |
| `ERR-012` | 最後のAdmin削除 | `400`、`LastAdminCannotBeDeleted` |
| `ERR-013` | ユーザー削除時のRunningジョブ | `409`、`UserHasRunningJobs` |
| `ERR-014` | エラー応答とログ | 秘密情報、本文全文、外部APIレスポンス全文が含まれない |

## 20. 関連ドキュメント

- [API設計書](api-design.md)
- [ジョブ設計書](job-design.md)
- [外部連携設計書](external-integration-design.md)
- [セキュリティ設計書](security-design.md)
- [データ保持・プライバシー設計書](data-retention-privacy.md)
- [テスト設計書](test-design.md)
