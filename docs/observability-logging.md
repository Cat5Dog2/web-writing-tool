# 観測性・ログ設計書

## 1. 目的

本書は、AIライティングツールの観測性、構造化ログ、メトリクス、ヘルスチェック、アラート、調査手順の方針を定義する。

対象は、Blazor Web App、ASP.NET Core Minimal API、BackgroundService、PostgreSQL、Caddy、Docker Compose、外部API連携、GitHub Actionsである。

## 2. 基本方針

- MVPでは追加の専用監視基盤を必須にせず、アプリケーションログ、Caddyアクセスログ、PostgreSQLログ、ヘルスチェック、外部Uptime監視、運用通知で開始する。
- アプリケーションログは`ILogger`による構造化ログを基本とし、コンテナ標準出力へ出す。
- ログ、メトリクス、ヘルスチェック、監査ログは用途を分ける。
- 障害調査では`traceId`、`jobId`、`articleId`、`userId`、`provider`、`errorCode`で追跡できるようにする。
- 秘密情報、プロンプト全文、記事本文全文、外部APIレスポンス全文をログへ出さない。
- 利用者向けエラー表示は[エラーコードリファレンス](error-codes.md)の`ErrorCode`と`traceId`を基準にする。
- 将来、SLA管理、長期分析、可視化要件が強くなった段階でOpenTelemetry、Prometheus、Loki、Grafana、APMサービスを検討する。

## 3. 観測対象

| 対象 | 主な観測内容 |
| --- | --- |
| Caddy | HTTPS疎通、アクセスログ、ステータスコード、TLS証明書 |
| ASP.NET Core App | API応答、認証、認可、例外、ProblemDetails、設定読込 |
| BackgroundService | ジョブ登録、開始、成功、再試行、失敗、滞留、Worker状態 |
| PostgreSQL | 接続可否、Migration、低速クエリ、エラー、バックアップ |
| 外部API | Gemini、Tavily、X、WordPress、Discordの失敗率、遅延、レート制限 |
| CI/CD | テスト結果、E2E成果物、Docker build、Migration確認、脆弱性確認 |
| VPS | CPU、メモリ、ディスク、Dockerコンテナ状態 |

## 4. シグナル種別

| 種別 | 用途 | 保存先 |
| --- | --- | --- |
| アクセスログ | HTTPリクエストの入口を確認する | Caddyログ |
| アプリケーションログ | API、画面、Applicationサービスの挙動を追跡する | `app`コンテナ標準出力 |
| ジョブログ | BackgroundServiceの非同期処理を追跡する | `app`コンテナ標準出力、DBのジョブ状態 |
| 外部連携ログ | Providerごとの失敗、遅延、レート制限を追跡する | `app`コンテナ標準出力、連携履歴テーブル |
| 監査ログ | 誰が何を変更したかを後から確認する | DBの`AuditLogs` |
| メトリクス | 傾向、しきい値、劣化を検知する | MVPではログと外部監視を中心に扱う |
| ヘルスチェック | 死活、Readiness、依存先状態を判定する | HTTP endpoint |

監査ログは操作証跡であり、デバッグ用の詳細ログではない。運用ログは短期保持の調査用であり、監査ログの代替にしない。

## 5. MVP構成

| 項目 | 方針 |
| --- | --- |
| アプリログ | JSONまたは構造化ログを標準出力へ出す |
| Caddyログ | JSON access logをファイル出力し、ローテーションする |
| PostgreSQLログ | エラー、接続、低速クエリを必要範囲で出力し、ローテーションする |
| コンテナログ | Docker `json-file` driverの`max-size`、`max-file`を設定する |
| 死活監視 | 外部Uptime監視サービスから`/health/live`を確認する |
| Readiness監視 | `/health/ready`を運用確認、デプロイ後確認、監視通知に使う |
| 通知 | 管理者メールまたは運用専用Discord Webhookへ通知する |
| ログ保持 | MVPでは30日を基本とする |

運用通知用Discord Webhookは、利用者が登録する通知設定とは分離する。運用通知先はデプロイ環境の秘密情報として管理し、ログへ出さない。

## 6. 構造化ログ共通項目

| 項目 | 必須 | 内容 |
| --- | --- | --- |
| `timestamp` | 必須 | UTCの発生時刻 |
| `level` | 必須 | `Debug`、`Information`、`Warning`、`Error`、`Critical` |
| `eventName` | 必須 | 安定したイベント名 |
| `traceId` | 条件付き | HTTPリクエストまたは処理単位の追跡ID |
| `correlationId` | 任意 | 外部指定または複数処理横断の相関ID |
| `userId` | 条件付き | 対象ユーザーID。メールアドレスは出さない |
| `jobId` | 条件付き | ジョブID |
| `articleId` | 条件付き | 記事ID |
| `headingId` | 条件付き | 見出しID |
| `provider` | 条件付き | `Gemini`、`Tavily`、`X`、`WordPress`、`Discord` |
| `operation` | 条件付き | 外部連携または業務処理名 |
| `statusCode` | 条件付き | HTTPステータスまたはProvider応答コード |
| `elapsedMs` | 条件付き | 処理時間 |
| `attemptCount` | 条件付き | ジョブまたは外部API再試行回数 |
| `errorCode` | 条件付き | [エラーコードリファレンス](error-codes.md)の`ErrorCode` |

`userId`は識別子として扱い、メールアドレス、表示名、ユーザー入力本文をログの検索キーにしない。長期保存や外部転送を行う段階では、`userId`のハッシュ化または匿名化を検討する。

## 7. Trace / Correlation

HTTPリクエストではASP.NET Coreの`TraceIdentifier`または`Activity.TraceId`を`traceId`として使う。APIエラー応答のProblemDetailsには同じ`traceId`を含める。

`X-Correlation-ID`を受け付ける場合は、長さ、文字種、形式を検証する。不正な値、長すぎる値、制御文字を含む値は破棄し、サーバー側で新しい値を発行する。

HTTPリクエストからジョブを登録する場合は、登録時の`traceId`または`correlationId`をジョブメタデータへ保存できる設計にする。ただし、ジョブ実行時の主追跡キーは`jobId`とする。

## 8. ログレベル

| Level | 用途 |
| --- | --- |
| `Debug` | 開発時の詳細確認。本番既定では抑制する |
| `Information` | 通常の状態遷移、API完了、ジョブ開始/成功、通知成功 |
| `Warning` | 再試行可能な外部API失敗、429、タイムアウト、低速処理、入力起因の頻発エラー |
| `Error` | 最終失敗、5xx、DB接続不可、ジョブ失敗確定、WordPress/Discord最終失敗 |
| `Critical` | アプリ起動不能、秘密情報漏えい疑い、Data Protection Key不備、DB破損疑い |

`Debug`ログは秘密情報を含めない。本番で一時的に有効化する場合も、出力期間と対象カテゴリを限定する。

## 9. イベント名

| EventName | 発生タイミング |
| --- | --- |
| `ApiRequestCompleted` | APIリクエスト完了 |
| `ProblemDetailsReturned` | エラー応答返却 |
| `UserLoginSucceeded` | ログイン成功 |
| `UserLoginFailed` | ログイン失敗 |
| `JobQueued` | ジョブ登録 |
| `JobStarted` | ジョブ開始 |
| `JobSucceeded` | ジョブ成功 |
| `JobRetryScheduled` | 再試行予定登録 |
| `JobFailed` | ジョブ最終失敗 |
| `JobCanceled` | ジョブキャンセル |
| `ExternalApiRequestCompleted` | 外部API成功 |
| `ExternalApiRequestFailed` | 外部API失敗 |
| `WordpressPostSucceeded` | WordPress投稿成功 |
| `WordpressPostFailed` | WordPress投稿失敗 |
| `DiscordNotificationSucceeded` | Discord通知成功 |
| `DiscordNotificationFailed` | Discord通知失敗 |
| `CacheCleanupCompleted` | 期限切れデータ削除成功 |
| `CacheCleanupFailed` | 期限切れデータ削除失敗 |
| `HealthCheckFailed` | ヘルスチェック失敗 |

イベント名は分析、アラート、テストで参照するため、安易に変更しない。

## 10. APIログ

API完了ログには以下を含める。

| 項目 | 方針 |
| --- | --- |
| path | ルートテンプレートを優先し、過度に長いクエリ文字列を出さない |
| method | HTTP method |
| statusCode | 応答ステータス |
| elapsedMs | 処理時間 |
| traceId | ProblemDetailsと一致させる |
| userId | 認証済みの場合のみ |
| errorCode | エラー時のみ |

リクエストBody、レスポンスBody、Cookie、Authorizationヘッダー、CSRF Tokenは出力しない。入力検証エラーでは、フィールド名と`ErrorCode`を出してよいが、入力値そのものは必要最小限にする。

## 11. ジョブログ

ジョブ関連ログには以下を含める。

| 項目 | 方針 |
| --- | --- |
| `jobId` | 必須 |
| `jobType` | 必須 |
| `articleId` | 対象がある場合 |
| `headingId` | 対象がある場合 |
| `userId` | 対象ユーザー |
| `attemptCount` | 実行回数 |
| `elapsedMs` | 開始から完了までの時間 |
| `errorCode` | 失敗時 |

`JobStarted`、`JobSucceeded`、`JobRetryScheduled`、`JobFailed`を状態遷移ごとに出力する。`ErrorMessage`は画面表示できる短い概要だけとし、詳細な例外はログへ出す場合でも秘密情報をマスクする。

## 12. 外部連携ログ

外部連携ログには以下を含める。

| 項目 | 方針 |
| --- | --- |
| `provider` | 必須 |
| `operation` | 必須 |
| `jobId` | ジョブ実行時 |
| `articleId` | 対象記事がある場合 |
| `statusCode` | HTTP応答がある場合 |
| `elapsedMs` | 必須 |
| `attemptCount` | 再試行時 |
| `errorCode` | 失敗時 |

Authorizationヘッダー、APIキー、Bearer Token、Application Password、Webhook URL、外部APIレスポンス全文、プロンプト全文、記事本文全文は出力しない。必要な場合は、件数、文字数、ハッシュ、Providerのエラー分類、HTTPステータスだけを記録する。

## 13. 監査ログ

監査ログはDBへ保存し、以下の操作を対象にする。

- ログイン成功/失敗
- 管理者によるユーザー作成、ロール変更、無効化、削除
- WordPressサイト登録、更新、削除、接続テスト
- Discord通知設定更新、送信テスト
- WordPress投稿
- ジョブキャンセル、再実行
- strict辞書更新
- 人間確認完了

監査ログには秘密情報、記事本文全文、プロンプト全文、X投稿本文の長期保持対象外データ、外部APIレスポンス全文を保存しない。

## 14. メトリクス候補

MVPではメトリクス専用基盤を必須にしない。将来Prometheusなどを導入する場合、以下を収集対象にする。

| メトリクス | 内容 |
| --- | --- |
| `http_requests_total` | HTTPリクエスト数 |
| `http_request_duration_seconds` | HTTP応答時間 |
| `http_server_errors_total` | 5xx件数 |
| `background_jobs_queued` | 待機中ジョブ数 |
| `background_jobs_running` | 実行中ジョブ数 |
| `background_jobs_failed_total` | 失敗ジョブ数 |
| `background_job_duration_seconds` | ジョブ処理時間 |
| `background_job_retry_total` | 再試行回数 |
| `external_api_requests_total` | 外部API呼び出し数 |
| `external_api_errors_total` | 外部API失敗数 |
| `external_api_rate_limited_total` | 外部API 429件数 |
| `external_api_duration_seconds` | 外部API応答時間 |
| `wordpress_post_failed_total` | WordPress投稿失敗数 |
| `discord_notification_failed_total` | Discord通知失敗数 |
| `search_cache_hit_total` | 検索キャッシュヒット数 |
| `search_cache_miss_total` | 検索キャッシュミス数 |
| `x_rehydration_failed_total` | X再取得失敗数 |
| `x_monthly_posts_used` | X API月間取得件数 |
| `db_connection_errors_total` | DB接続失敗数 |
| `db_query_duration_seconds` | DBクエリ時間 |

## 15. ヘルスチェック

| Endpoint | 用途 | 確認内容 | 公開範囲 |
| --- | --- | --- | --- |
| `/health/live` | Liveness | プロセス生存。DBや外部APIは呼ばない | 外部監視可 |
| `/health/ready` | Readiness | PostgreSQL接続、軽量クエリ、Worker状態 | 内部または監視限定 |
| `/health/deps` | 依存先確認 | Gemini、Tavily、X、WordPress、Discordの簡易疎通 | 管理者限定 |

`/health/live`は外部API障害で失敗させない。`/health/ready`はデプロイ後確認、コンテナ起動判定、運用監視に使う。`/health/deps`は重くなりやすいため、タイムアウトを短くし、通常の死活監視に使わない。

ヘルスチェック応答には接続文字列、APIキー、Webhook URL、Application Password、外部APIレスポンス全文を含めない。

## 16. アラート

| 分類 | 条件 | 通知先 |
| --- | --- | --- |
| 死活 | HTTPS応答が2回連続失敗 | 管理者メールまたは運用Discord |
| Readiness | `/health/ready`が2回連続失敗 | 管理者メールまたは運用Discord |
| DB | DB接続不可が1分以上継続 | 管理者メールまたは運用Discord |
| リソース | CPU 80%以上が10分継続 | 管理者メール |
| リソース | メモリ85%以上が10分継続 | 管理者メール |
| リソース | ディスク80%以上 | 管理者メール |
| ジョブ | `Failed`が1時間で5件以上 | 運用Discord |
| ジョブ | `Queued`が30分以上滞留 | 運用Discord |
| 外部API | 429またはレート制限が連続発生 | 運用Discord |
| X API | 月間取得件数が安全上限に接近 | 運用Discord |
| TLS | 証明書期限が14日未満 | 管理者メール |
| バックアップ | DBバックアップ失敗 | 管理者メールまたは運用Discord |
| セキュリティ | 秘密情報漏えい疑い | 管理者メール |

ジョブ失敗通知では、`jobId`、`jobType`、`errorCode`、`provider`、発生時刻だけを含める。記事本文、プロンプト、Webhook URLは含めない。

## 17. ログ保持・ローテーション

| 対象 | 方針 |
| --- | --- |
| Dockerログ | `json-file`の`max-size`、`max-file`を設定する |
| Caddyアクセスログ | 日次またはサイズベースでローテーションする |
| PostgreSQLログ | サイズベースでローテーションする |
| アプリログ | コンテナログのローテーションに従う |
| CI成果物 | GitHub Actionsの保存期間に従い、失敗時成果物を中心に保存する |
| 監査ログ | [データ保持・プライバシー設計書](data-retention-privacy.md)に従う |

MVPの運用ログ保持は30日を基本とする。インシデント調査、監査、契約、プライバシー要件により変更する場合は、[運用設計書](operation-design.md)と[データ保持・プライバシー設計書](data-retention-privacy.md)を同時に更新する。

## 18. 出力禁止・マスク対象

以下をログ、監査ログ、CI成果物、ヘルスチェック応答、画面、通知へ出さない。

- Gemini API Key
- Tavily API Key
- X API Bearer Token
- DBパスワード
- WordPress Application Password
- Discord Webhook URL
- Authorizationヘッダー
- 認証Cookie
- CSRF Token
- Data Protection Key
- プロンプト全文
- 記事本文全文
- X投稿本文の長期保持対象外データ
- 外部APIレスポンス全文
- `.env`
- User Secrets

出力が必要な場合は、`***`によるマスク、件数、文字数、ハッシュ、短い分類名、`ErrorCode`で代替する。

## 19. CI/CDログと成果物

GitHub Actionsでは以下を徹底する。

- 外部本番APIを通常CIで呼ばない。
- テスト失敗ログに秘密情報を出さない。
- E2E失敗時のtrace、screenshot、videoには秘密情報、本文全文、プロンプト全文が映り込まないようにする。
- `.env`、User Secrets、本番Data Protection Keyを成果物へ含めない。
- Migration SQLを成果物化する場合、接続文字列や本番認証情報を含めない。

CI上で秘密情報漏えいが疑われる場合は、対象secretを無効化し、再発行し、成果物を削除する。

## 20. 障害調査手順

1. 発生時刻、利用者影響、`traceId`、`jobId`、`errorCode`を確認する。
2. `/health/live`、`/health/ready`、`docker compose ps`でサービス状態を確認する。
3. CaddyアクセスログでHTTPステータス、パス、発生頻度を確認する。
4. アプリログを`traceId`、`jobId`、`eventName`、`errorCode`で検索する。
5. ジョブ起因の場合は`ArticleGenerationJobs`、`WordpressPosts`、`NotificationLogs`の状態を確認する。
6. 外部API起因の場合はProvider、operation、statusCode、429、timeout、再試行回数を確認する。
7. DB起因の場合はPostgreSQLログ、接続数、Migration、ディスク使用量を確認する。
8. セキュリティ起因が疑われる場合は監査ログと秘密情報漏えい有無を確認する。
9. 暫定対応、恒久対応、再発防止、ドキュメント更新要否を記録する。

調査で取得したログを共有する場合は、秘密情報、本文、プロンプト、個人情報をマスクする。

## 21. テスト観点

| ID | 観点 | 期待結果 |
| --- | --- | --- |
| `OBS-001` | APIエラー応答 | ProblemDetailsに`traceId`と`errorCode`が含まれる |
| `OBS-002` | APIログ | `eventName`、`traceId`、`statusCode`、`elapsedMs`が出る |
| `OBS-003` | ジョブログ | `jobId`、`jobType`、`attemptCount`、`errorCode`が出る |
| `OBS-004` | 外部APIログ | `provider`、`operation`、`statusCode`、`elapsedMs`が出る |
| `OBS-005` | 秘密情報マスク | APIキー、Webhook URL、Application Passwordが出ない |
| `OBS-006` | 本文・プロンプト除外 | 記事本文全文、プロンプト全文、外部APIレスポンス全文が出ない |
| `OBS-007` | Trace伝播 | API応答、ログ、ジョブ登録情報を同一IDで追跡できる |
| `OBS-008` | `/health/live` | DBや外部APIを呼ばずに成功する |
| `OBS-009` | `/health/ready` | DB接続不可またはWorker停止で失敗する |
| `OBS-010` | `/health/deps` | 管理者限定で、秘密情報を返さない |
| `OBS-011` | CI成果物 | E2E trace、screenshot、videoに秘密情報が含まれない |
| `OBS-012` | ローテーション | Docker、Caddy、PostgreSQLログが肥大化し続けない |

## 22. 受け入れ基準

- APIエラー調査に必要な`traceId`と`errorCode`が返る。
- ジョブ失敗を`jobId`、`jobType`、`errorCode`で追跡できる。
- 外部API失敗を`provider`、`operation`、`statusCode`、`elapsedMs`で追跡できる。
- `/health/live`、`/health/ready`、`/health/deps`の役割が分離されている。
- 運用通知は秘密情報と本文全文を含まない。
- ログ保持とローテーションの方針がある。
- CI成果物に秘密情報、プロンプト全文、記事本文全文、外部APIレスポンス全文が含まれない。
- 監査ログと運用ログの役割が分離されている。

## 23. 関連ドキュメント

- [要件定義書](requirements.md)
- [基本設計書](basic-design.md)
- [API設計書](api-design.md)
- [エラーコードリファレンス](error-codes.md)
- [ジョブ設計書](job-design.md)
- [外部連携設計書](external-integration-design.md)
- [セキュリティ設計書](security-design.md)
- [データ保持・プライバシー設計書](data-retention-privacy.md)
- [テスト設計書](test-design.md)
- [CI/CD設計書](ci-cd-design.md)
- [運用設計書](operation-design.md)
- [設定リファレンス](configuration-reference.md)
