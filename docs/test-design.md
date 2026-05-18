# テスト設計書

## 1. 目的

本書は、本システムの品質を検証するためのテスト方針、テスト範囲、テスト種別、観点、実行方法を定義する。
対象は、Blazor Web App、ASP.NET Core Minimal API、EF Core/PostgreSQL、BackgroundService、外部API連携、Docker Compose環境である。

AI生成やWordPress投稿など外部依存が多いため、単体テスト、結合テスト、E2Eテスト、外部APIモックを組み合わせて検証する。

## 2. 基本方針

- テストは層ごとに責務を分ける。
- 純粋な業務ロジックは単体テストで高速に検証する。
- API、DI、認証、EF Core、PostgreSQL、BackgroundServiceは結合テストで検証する。
- 主要ユーザーフローはPlaywrightによるE2Eテストで検証する。
- 外部本番APIは自動テストで直接呼び出さない。
- PostgreSQL依存の挙動は、EF Core InMemoryではなくPostgreSQLまたはそれに近いDBで検証する。
- 秘密情報、APIキー、Application Passwordをテストログへ出さない。
- 失敗系、再試行、権限不足、外部API障害を重視する。

## 3. テスト対象範囲

| 領域 | 主な対象 |
| --- | --- |
| Domain | enum、状態遷移、値オブジェクト |
| Application | 記事作成、見出し操作、利用上限、HTML変換 |
| Infrastructure | EF Core、外部API Client、暗号化、ファイル保存 |
| Web/API | Minimal API、認証/認可、ProblemDetails |
| Background | ジョブ登録、取得、排他、リトライ、結果保存 |
| UI | Blazor画面、フォーム、モーダル、ジョブ状態表示 |
| Deploy | Docker Compose、health check、PostgreSQL接続 |

## 4. テスト種別

| 種別 | 目的 | 主なツール | 実行頻度 |
| --- | --- | --- | --- |
| 単体テスト | 業務ロジックを高速検証 | xUnit | 常時 |
| コンポーネントテスト | Blazorコンポーネント単体検証 | bUnit + xUnit | 必要時 |
| 結合テスト | API、DI、DB、認証、外部モック検証 | xUnit + WebApplicationFactory | PRごと |
| DBテスト | PostgreSQL固有挙動検証 | xUnit + Testcontainers for .NET | PRごと |
| ジョブテスト | BackgroundService、ロック、リトライ検証 | xUnit + PostgreSQL | PRごと |
| E2Eテスト | 主要画面フロー検証 | xUnit + Playwright for .NET | PRは最小セット / main・夜間は全件 |
| 手動受け入れ | 画像に近いUI・操作性確認 | ブラウザ | リリース前 |

### 4.1 テストフレームワーク方針

テストフレームワークはxUnitに固定する。単体テスト、結合テスト、DBテスト、ジョブテスト、E2EテストでNUnit/MSTestを混在させない。

| 種別 | 方針 |
| --- | --- |
| 単体テスト | xUnit |
| 結合テスト | xUnit + `WebApplicationFactory` |
| DBテスト | xUnit + PostgreSQL |
| ジョブテスト | xUnit |
| コンポーネントテスト | bUnit + xUnit |
| E2Eテスト | Playwright for .NETをxUnitランナーで実行 |

新規テストプロジェクトではxUnit v3を第一候補とし、導入時点の.NET SDK、Playwright、bUnit、CI環境との互換性に問題がある場合のみxUnit v2を選択する。

### 4.2 Blazorコンポーネントテスト方針

BlazorコンポーネントテストにはbUnitを採用する。ただしMVPでは対象を再利用コンポーネントと状態表示に絞り、主要ユーザーフローはPlaywright E2Eで検証する。

| 項目 | 方針 |
| --- | --- |
| 採用ツール | bUnit |
| テストランナー | xUnit |
| MVP対象 | 再利用コンポーネント、入力フォーム、モーダル、状態表示 |
| 優先対象 | `UsageSummary`、`ArticleSearchForm`、`BulkCreateModal`、`WordpressPostModal`、`JobStatusBadge` |
| 検証観点 | パラメータ反映、入力バリデーション、条件付き表示、ボタン有効/無効、イベント発火 |
| 対象外 | 画面全体の業務フロー、ブラウザ固有挙動、JS依存挙動、ドラッグ操作 |
| 主要フロー | Playwright E2Eで検証 |

## 5. テストプロジェクト構成

```text
tests/
  WebWritingTool.UnitTests/
    Domain/
    Application/
    Infrastructure/
  WebWritingTool.IntegrationTests/
    Api/
    Data/
    Jobs/
    ExternalIntegrations/
  WebWritingTool.E2ETests/
    Articles/
    Settings/
    Wordpress/
```

## 6. テストデータ方針

### 6.1 ユーザー

| ユーザー | ロール | 用途 |
| --- | --- | --- |
| `admin@example.com` | Admin | 管理者API、全記事参照 |
| `user1@example.com` | User | 通常操作 |
| `user2@example.com` | User | 所有者チェック |
| `delete-target@example.com` | User | 管理者による物理削除 |
| `disabled@example.com` | User | 無効ユーザー |

### 6.2 記事

| データ | 用途 |
| --- | --- |
| Draft記事 | 編集、構成生成前 |
| OutlineReady記事 | 本文生成 |
| Completed記事 | WordPress投稿 |
| Posted記事 | 投稿済み表示 |
| Failed記事 | 再実行、エラー表示 |
| Deleted記事 | 論理削除除外 |

### 6.3 ジョブ

| データ | 用途 |
| --- | --- |
| Queued | 取得対象 |
| Running | ロック中、削除不可 |
| Succeeded | 履歴表示 |
| Failed | 再実行 |
| Canceled | キャンセル表示 |

## 7. 単体テスト設計

### 7.1 Domain

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `UT-DOM-001` | ArticleStatus | 構成生成、本文生成、投稿の状態遷移 |
| `UT-DOM-002` | HeadingStatus | 生成中、生成済み、失敗の遷移 |
| `UT-DOM-003` | JobStatus | QueuedからRunning/Succeeded/Failed/Canceled |
| `UT-DOM-004` | タグ処理 | カンマ区切り、空白除去、重複除去 |
| `UT-DOM-005` | 文字数計算 | 日本語、改行、記号を含む文字数 |

### 7.2 Application

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `UT-APP-001` | 一括登録パーサー | `キーワード`形式 |
| `UT-APP-002` | 一括登録パーサー | `キーワード|タイトル`形式 |
| `UT-APP-003` | 一括登録パーサー | 空行、不正行、重複 |
| `UT-APP-004` | 構成生成回数判定 | 残数あり |
| `UT-APP-005` | 構成生成回数判定 | 残数なし |
| `UT-APP-006` | HTML変換 | H2/H3/段落変換 |
| `UT-APP-007` | HTML変換 | 句点改行 |
| `UT-APP-008` | プロンプト生成 | タイトル候補 |
| `UT-APP-009` | プロンプト生成 | 見出し構成 |
| `UT-APP-010` | プロンプト生成 | 本文生成、追加プロンプト |
| `UT-APP-011` | 一括登録自動投稿 | 自動投稿有効時のみ投稿先サイトとカテゴリを記事へ保存 |
| `UT-APP-012` | 自動投稿ジョブ登録 | 本文生成完了時にDraftのWordpressPostジョブを重複なく登録 |
| `UT-APP-013` | サイト別ライティング設定 | 管理人プロフィール、キャラ設定、読者ペルソナがプロンプトへ反映される |
| `UT-APP-014` | サイト別ライティング設定 | 記事作成時に設定スナップショットが保存される |

### 7.3 Infrastructure

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `UT-INF-001` | ErrorCode変換 | HTTP 429 -> RateLimited |
| `UT-INF-002` | ErrorCode変換 | HTTP 401 -> UnauthorizedExternalApi |
| `UT-INF-003` | RetryPolicy | 再試行可否 |
| `UT-INF-004` | RetryPolicy | NextRunAt計算 |
| `UT-INF-005` | URL検証 | HTTPS許可 |
| `UT-INF-006` | URL検証 | localhost拒否 |
| `UT-INF-007` | URL検証 | プライベートIP拒否 |
| `UT-INF-008` | 通知本文 | 秘密情報が含まれない |
| `UT-INF-009` | WordPress DTO | 投稿リクエスト生成 |
| `UT-INF-010` | WordPress DTO | 一括自動投稿でも投稿ステータスがDraftになる |

## 8. API結合テスト設計

### 8.1 方針

- `Microsoft.AspNetCore.Mvc.Testing`と`WebApplicationFactory<Program>`を使用する。
- 認証済みユーザーを差し替えられるテスト認証Handlerを用意する。
- 外部API Clientはテストダブルへ差し替える。
- リダイレクトを自動追跡せず、認証/認可ステータスを検証する。

### 8.2 Articles API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-001` | `GET /api/articles` | 未認証 | 401 |
| `IT-API-002` | `GET /api/articles` | 認証済み | 自分の記事のみ |
| `IT-API-003` | `GET /api/articles` | 管理者 | 全記事検索可 |
| `IT-API-004` | `POST /api/articles` | 正常 | 201 |
| `IT-API-005` | `POST /api/articles` | キーワード未入力 | 400 ProblemDetails |
| `IT-API-006` | `PUT /api/articles/{id}` | 他ユーザー記事 | 403または404 |
| `IT-API-007` | `DELETE /api/articles/{id}` | Runningジョブあり | 409 |
| `IT-API-008` | `DELETE /api/articles/{id}` | 正常 | 204、論理削除 |

### 8.3 Headings API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-020` | `GET /api/articles/{id}/headings` | 所有者 | 200 |
| `IT-API-021` | `POST /api/articles/{id}/headings` | H2追加 | 201 |
| `IT-API-022` | `POST /api/articles/{id}/headings` | H3の親が別記事 | 400または422 |
| `IT-API-023` | `PUT /api/articles/{id}/headings/order` | 並び替え | 200 |
| `IT-API-024` | `DELETE /api/articles/{id}/headings/{headingId}` | 生成中 | 409 |

### 8.4 Jobs API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-040` | `POST /generation/outline` | 正常登録 | 202、jobId |
| `IT-API-041` | `POST /generation/outline` | 利用上限超過 | 422 |
| `IT-API-042` | `POST /generation/body` | 多重実行 | 409 |
| `IT-API-043` | `GET /api/jobs/{id}` | 所有者 | 200 |
| `IT-API-044` | `GET /api/jobs/{id}` | 他ユーザー | 403または404 |
| `IT-API-045` | `POST /api/jobs/{id}/cancel` | Queued | 200、Canceled |
| `IT-API-046` | `POST /api/jobs/{id}/retry` | Failed | 202、新jobId |

### 8.5 WordPress / Notifications API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-060` | `POST /api/wordpress-sites` | APP-PASS登録 | レスポンスに秘密情報なし |
| `IT-API-061` | `GET /api/wordpress-sites` | 一覧 | APP-PASSなし |
| `IT-API-062` | `POST /api/wordpress-sites/{id}/test` | 接続失敗 | 200、success false |
| `IT-API-063` | `POST /api/articles/{id}/wordpress-posts` | 投稿ジョブ登録 | 202 |
| `IT-API-064` | `POST /api/articles/bulk` | WordPress自動投稿指定で記事に自動投稿設定を保存 | 202 |
| `IT-API-065` | `POST /api/articles/bulk` | 他ユーザーのWordPressサイトID指定 | 403または404 |
| `IT-API-066` | `PUT /api/notifications/settings` | 保存 | 200 |
| `IT-API-067` | `POST /api/notifications/test` | 送信テスト | 200 |
| `IT-API-068` | `POST /api/wordpress-sites` | ライティング設定保存 | 201、APP-PASSなし |
| `IT-API-069` | `POST /api/articles` | 他ユーザーのライティング設定サイトID指定 | 403または404 |
| `IT-API-070` | `POST /api/articles` | generateImage true | 400 |

### 8.6 Admin API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-079` | `GET /api/admin/users` | 管理者がユーザー一覧取得 | 200 |
| `IT-API-080` | `POST /api/admin/users` | 管理者がUser作成 | 201、パスワードはレスポンスに含まれない |
| `IT-API-081` | `POST /api/admin/users` | 管理者がAdmin作成 | 201、Adminロール付与 |
| `IT-API-082` | `PUT /api/admin/users/{userId}/role` | UserをAdminへ昇格 | 200 |
| `IT-API-083` | `PUT /api/admin/users/{userId}/role` | 最後のAdminを降格 | 400 |
| `IT-API-084` | `DELETE /api/admin/users/{userId}` | 一般ユーザー | 403 |
| `IT-API-085` | `DELETE /api/admin/users/{userId}` | 管理者が他ユーザー削除 | 204、関連データ物理削除 |
| `IT-API-086` | `DELETE /api/admin/users/{userId}` | 自分自身を削除 | 400 |
| `IT-API-087` | `DELETE /api/admin/users/{userId}` | 最後のAdmin削除 | 400 |
| `IT-API-088` | `DELETE /api/admin/users/{userId}` | Runningジョブあり | 409 |
| `IT-API-089` | `PUT /api/admin/users/{userId}` | 表示名、有効状態更新 | 200 |
| `IT-API-090` | `PUT /api/admin/users/{userId}` | 最後のAdmin無効化 | 400 |
| `IT-API-091` | `PUT /api/admin/users/{userId}/usage-limit` | 利用上限更新 | 200 |
| `IT-API-092` | `GET /api/admin/audit-logs` | 監査ログ取得 | 200 |

### 8.7 Account API

| テストID | API | 観点 | 期待 |
| --- | --- | --- | --- |
| `IT-API-093` | `DELETE /api/account` | 本人退会 | 204、関連データ物理削除、Cookie破棄 |
| `IT-API-094` | `DELETE /api/account` | パスワード不一致 | 400 |
| `IT-API-095` | `DELETE /api/account` | 最後のAdmin | 400 |
| `IT-API-096` | `DELETE /api/account` | Runningジョブあり | 409 |

## 9. DB結合テスト設計

### 9.1 方針

- PostgreSQL固有機能を使うため、PostgreSQLで検証する。
- PostgreSQL結合テストはTestcontainers for .NETを第一候補とする。
- Docker ComposeのテストDBは、本番相当構成の確認、E2E、手動検証で使用する。
- EF Core InMemory ProviderはDB制約、SQL、トランザクション、PostgreSQL型の検証には使用しない。

### 9.2 PostgreSQLテストDB方針

| 項目 | 方針 |
| --- | --- |
| 第一候補 | Testcontainers for .NET |
| 対象 | API結合テスト、DB結合テスト、ジョブ結合テスト |
| DB | 実PostgreSQLコンテナ |
| 初期化 | テスト開始時にMigration適用またはスキーマ作成を行う |
| データ分離 | テストクラスまたはテストコレクション単位でDBを初期化する |
| Docker Compose | 本番相当のCompose起動確認、E2E、手動検証で使用する |
| 禁止 | PostgreSQL依存テストにEF Core InMemory Providerを使わない |

### 9.3 テスト観点

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `IT-DB-001` | Migrations | 初期マイグレーションが適用できる |
| `IT-DB-002` | Articles | `UserId`, `CreatedAt`で一覧取得できる |
| `IT-DB-003` | Articles | 論理削除が通常検索から除外される |
| `IT-DB-004` | ArticleHeadings | H2/H3階層を順序通り取得できる |
| `IT-DB-005` | ArticleHeadings | 別記事H2を親にできない |
| `IT-DB-006` | UsageLedgers | AI生成ごとの利用履歴を保存できる |
| `IT-DB-007` | WordpressSites | Application Passwordが平文保存されない |
| `IT-DB-008` | NotificationSettings | ユーザー/Providerで有効設定が一意 |
| `IT-DB-009` | RowVersion | 同時更新競合を検出できる |
| `IT-DB-010` | Tags | `text[]`とGIN検索が動作する |
| `IT-DB-011` | SearchResults | QueryHashとCacheExpiresAtでキャッシュ判定できる |
| `IT-DB-012` | XSearchPosts | PostIdで同一投稿を重複保存できない |
| `IT-DB-013` | SearchResults | Tavilyデータ種別ごとの保持期限を保存できる |
| `IT-DB-014` | XSearchPosts | X投稿生データの保持期限を保存できる |
| `IT-DB-015` | User hard delete | 管理者削除と本人退会で対象ユーザーの業務データが物理削除される |
| `IT-DB-016` | WordpressSites | ライティング設定を保存、更新できる |
| `IT-DB-017` | Articles | WritingProfileSnapshotJsonを保存できる |
| `IT-DB-018` | Articles | 本文履歴を作らず現在値を更新できる |
| `IT-DB-019` | Migrations | 画像メタデータ用テーブル、画像URLカラムを作成しない |
| `IT-DB-020` | Migrations | WordPressカテゴリ一覧キャッシュテーブルを作成しない |

## 10. BackgroundService / ジョブテスト設計

### 10.1 方針

- Handler単体は外部Clientをモックして検証する。
- ジョブ取得、ロック、状態遷移はPostgreSQLを使って検証する。
- Workerの無限ループはテストしづらいため、取得・Dispatch・状態更新の単位に分けて検証する。

### 10.2 ジョブ登録

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `IT-JOB-001` | OutlineGeneration登録 | ArticleとJobが作成される |
| `IT-JOB-002` | BodyGeneration登録 | HeadingId付きJobが作成される |
| `IT-JOB-003` | 多重登録防止 | 同一HeadingのRunningがあると409 |
| `IT-JOB-004` | 利用上限 | 上限超過でJobを作らない |

### 10.3 ジョブ取得・排他

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `IT-JOB-020` | JobLeaseService | Queuedを1件取得しRunningへ更新 |
| `IT-JOB-021` | 排他制御 | 2ワーカー同時取得で同一Jobを取らない |
| `IT-JOB-022` | 優先度 | Priority DESC, QueuedAt ASCで取得 |
| `IT-JOB-023` | NextRunAt | 未来時刻のJobは取得しない |
| `IT-JOB-024` | ロック期限 | 期限切れRunningをQueuedへ戻す |

### 10.4 Handler成功系

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `IT-JOB-040` | TitleGeneration | ResultJsonに候補が保存される |
| `IT-JOB-041` | OutlineGeneration | ArticleHeadingsが保存される |
| `IT-JOB-042` | BodyGeneration | Heading.Bodyが保存される |
| `IT-JOB-043` | Rewrite | 本文履歴を作らず元本文が置き換わる |
| `IT-JOB-044` | WordpressPost | WordpressPostsとArticles.Statusが更新される |
| `IT-JOB-045` | Notification | NotificationLogsが保存される |
| `IT-JOB-046` | WebSearch | Tavily検索結果がSearchResultsへ保存される |
| `IT-JOB-047` | XFullArchiveSearch | X投稿がXSearchPostsへ保存される |
| `IT-JOB-048` | WordpressPost | status未指定時にDraftで投稿される |
| `IT-JOB-049` | BulkAutoPost | BodyGeneration完了後にWordpressPostジョブが自動登録される |
| `IT-JOB-050` | BulkAutoPost | AutoPostQueuedAt設定済みまたは投稿履歴ありなら二重登録しない |
| `IT-JOB-051` | BodyGeneration | サイト別ライティング設定スナップショットをプロンプト入力へ反映する |

### 10.5 Handler失敗・リトライ

| テストID | 対象 | 観点 |
| --- | --- | --- |
| `IT-JOB-060` | RateLimited | Queuedへ戻りNextRunAt設定 |
| `IT-JOB-061` | Timeout | MaxAttempts未満なら再試行 |
| `IT-JOB-062` | UnauthorizedExternalApi | Failed、再試行なし |
| `IT-JOB-063` | ValidationError | Failed、再試行なし |
| `IT-JOB-064` | MaxAttempts到達 | Failed |
| `IT-JOB-065` | WordPress投稿失敗 | ArticleはCompleted維持 |
| `IT-JOB-066` | AI成功後保存失敗 | UsageLedgersを二重記録しない |
| `IT-JOB-067` | WebSearchキャッシュ | 有効キャッシュがある場合はTavilyを呼ばない |
| `IT-JOB-068` | X投稿重複 | 同じPostIdは再保存しない |
| `IT-JOB-069` | X投稿保持期限 | 環境別TTL超過の本文を削除または匿名化する |
| `IT-JOB-070` | X引用再検証 | WordPress投稿前に引用元投稿を再hydrationする |
| `IT-JOB-071` | 環境別TTL | dev/staging/production/strictのTTLが設定通り適用される |
| `IT-JOB-072` | TTL解決 | 環境、ユーザー、記事、データソース、トピックのうち最短TTLが採用される |
| `IT-JOB-073` | TTL緩和禁止 | ユーザー/記事設定で環境TTLより長くできない |
| `IT-JOB-074` | トピックstrict | news/trend/legal/price等で短TTLが適用される |
| `IT-JOB-075` | compliance_strict | 人間確認必須フラグが立つ |
| `IT-JOB-076` | compliance_strict公開抑止 | 人間確認前は公開投稿できない |
| `IT-JOB-077` | 辞書優先順位 | strictとcompliance_strictの両方に一致した場合compliance_strictになる |
| `IT-JOB-078` | normal判定 | 一般SEO、ハウツー、技術メモはnormalになる |
| `IT-JOB-079` | 除外キーワード | 除外キーワードに一致した場合は過剰なstrict判定を避ける |
| `IT-JOB-080` | 辞書更新 | YAML/JSON更新後の判定結果が期待通りになる |

## 11. 外部APIモック設計

### 11.1 方針

- 本番APIキーを自動テストで使わない。
- `IHttpClientFactory`にテスト用`HttpMessageHandler`を差し替える。
- Provider別Clientのレスポンス変換は固定JSONで検証する。
- タイムアウト、429、500、壊れたJSONを明示的にテストする。

### 11.2 AI Provider

| テストID | 観点 |
| --- | --- |
| `IT-EXT-001` | Gemini 3.1 Pro Preview成功レスポンスを共通DTOへ変換 |
| `IT-EXT-002` | Gemini APIのモデルIDが設定値から解決される |
| `IT-EXT-003` | 429をRateLimitedへ変換 |
| `IT-EXT-004` | 401をUnauthorizedExternalApiへ変換 |
| `IT-EXT-005` | JSONパース失敗をExternalBadResponseへ変換 |

### 11.3 Tavily / X Search

| テストID | 観点 |
| --- | --- |
| `IT-EXT-010` | Tavily成功レスポンスをSearchResultへ変換 |
| `IT-EXT-011` | Tavily 429をRateLimitedへ変換 |
| `IT-EXT-012` | Tavily検索条件が正規化される |
| `IT-EXT-013` | X Full-Archive Search成功レスポンスをXSearchPostへ変換 |
| `IT-EXT-014` | X検索で期間、言語、除外条件が設定される |
| `IT-EXT-015` | X 429をRateLimitedへ変換 |
| `IT-EXT-016` | X検索の通常max_resultsが100に制限される |
| `IT-EXT-017` | X検索の大量調査max_resultsが500に制限される |
| `IT-EXT-018` | 月間安全上限超過見込みでX検索を停止する |

### 11.4 WordPress

| テストID | 観点 |
| --- | --- |
| `IT-EXT-020` | 接続テスト成功 |
| `IT-EXT-021` | 接続テスト認証失敗 |
| `IT-EXT-022` | カテゴリ一覧をWordPress REST APIから都度取得する |
| `IT-EXT-023` | 投稿成功でPostId/PostUrl取得 |
| `IT-EXT-024` | 投稿失敗時にErrorCode保存 |
| `IT-EXT-025` | 投稿ステータス省略時にDraftになる |

### 11.5 Discord

| テストID | 観点 |
| --- | --- |
| `IT-EXT-040` | 通知送信成功 |
| `IT-EXT-041` | 通知送信失敗 |
| `IT-EXT-042` | 通知本文に秘密情報が含まれない |

## 12. Blazor UI / E2Eテスト設計

### 12.1 方針

- Playwright for .NETを使用する。
- テスト前にDBを既知状態へリセットする。
- 外部APIはモック応答を使用する。
- UIの細かな見た目より、主要業務フローと状態表示を優先する。

### 12.2 シナリオ

| テストID | シナリオ | 期待 |
| --- | --- | --- |
| `E2E-001` | ログイン | 記事一覧へ遷移 |
| `E2E-002` | 記事一覧検索 | 条件に一致する記事のみ表示 |
| `E2E-003` | 一括登録 | 複数記事とジョブが作成される |
| `E2E-004` | 記事作成 | キーワード入力から構成ジョブ登録 |
| `E2E-005` | タイトル候補 | 候補を選択してタイトル反映 |
| `E2E-006` | 生成結果編集 | 見出し選択、本文編集、保存 |
| `E2E-007` | HTML変換 | HTML本文が生成される |
| `E2E-008` | WordPress投稿 | 投稿モーダルからジョブ登録 |
| `E2E-009` | 通知設定 | 送信テスト成功 |
| `E2E-010` | 権限不足 | 他ユーザー記事へアクセス不可 |
| `E2E-011` | サイト別ライティング設定 | WordPressサイトに設定を保存し、記事作成で選択できる |

### 12.3 E2E実行範囲

Issue単位でmainから作業ブランチを切り、PR作成時にCIを実行する。PRでは開発速度を落とさない最小E2Eを必須チェックにし、mainマージ後または夜間CIで全E2Eを実行する。

| 実行タイミング | 対象 | 方針 |
| --- | --- | --- |
| PR | 最小セット | `E2E-001`、`E2E-002`、`E2E-004`、`E2E-006`、`E2E-010`をChromiumで実行する |
| mainマージ後 | 全件 | `E2E-001`から`E2E-011`までを実行し、PRで省略した投稿・通知・設定系を補完する |
| 夜間 | 全件 + 追加検証 | 全E2Eに加え、必要に応じてDocker Composeの本番相当構成で確認する |
| リリース前 | 全件 + 手動受け入れ | 全E2E成功後、画面崩れや操作性を手動で確認する |

PRの最小セットは、ログイン、記事検索、記事作成、生成結果編集、認可拒否を対象にする。外部APIはモック応答を使い、失敗時のみtrace、screenshot、videoを成果物として保存する。

### 12.4 レスポンシブ確認

| 幅 | 観点 |
| --- | --- |
| 1366px | 2カラム編集が崩れない |
| 1024px | テーブル、フォームが収まる |
| 390px | 1カラム表示、ボタン折り返し、モーダル表示 |

## 13. セキュリティテスト設計

| テストID | 観点 | 期待 |
| --- | --- | --- |
| `SEC-001` | 未認証APIアクセス | 401 |
| `SEC-002` | 他ユーザー記事更新 | 403または404 |
| `SEC-003` | 管理者APIへ一般ユーザーアクセス | 403 |
| `SEC-004` | WordPress APP-PASSレスポンス混入 | 含まれない |
| `SEC-005` | ログの秘密情報混入 | 含まれない |
| `SEC-006` | SSRF localhost URL | 拒否 |
| `SEC-007` | SSRF private IP | 拒否 |
| `SEC-008` | HTMLプレビューXSS | スクリプト実行されない |
| `SEC-009` | CSRF対象操作 | 保護される |
| `SEC-010` | 管理者ユーザー削除 | 自分自身と最後のAdminは削除できない |
| `SEC-011` | Admin追加 | Seedは既存Adminのパスワードを上書きせず、管理画面経由の昇格は監査ログに残る |
| `SEC-012` | 他ユーザーのライティング設定サイトID指定 | 403または404 |
| `SEC-013` | サイト別ライティング設定本文のログ混入 | プロンプト全文として出力されない |
| `SEC-014` | 本人退会 | 現在パスワード必須、最後のAdminとRunningジョブありは拒否 |

## 14. 非機能テスト設計

### 14.1 性能

| テストID | 対象 | 目安 |
| --- | --- | --- |
| `NFT-PERF-001` | 記事一覧 | 10件表示が1秒以内 |
| `NFT-PERF-002` | 記事検索 | 1,000件規模で2秒以内 |
| `NFT-PERF-003` | 見出し取得 | 100見出しで2秒以内 |
| `NFT-PERF-004` | ジョブ取得 | Queued 10,000件で取得が安定 |

### 14.2 可用性・復旧

| テストID | 対象 | 期待 |
| --- | --- | --- |
| `NFT-AVL-001` | アプリ再起動 | Queuedジョブが残る |
| `NFT-AVL-002` | Worker停止 | Running期限切れを復旧できる |
| `NFT-AVL-003` | PostgreSQL再起動 | 復旧後に接続できる |
| `NFT-AVL-004` | 外部API障害 | 再試行またはFailedへ遷移 |

### 14.3 ヘルスチェック

| テストID | Path | 観点 |
| --- | --- | --- |
| `NFT-HC-001` | `/health/live` | アプリ生存 |
| `NFT-HC-002` | `/health/ready` | PostgreSQL接続 |
| `NFT-HC-003` | `/health/ready` | Worker状態 |

## 15. Docker / CIテスト

### 15.1 ローカル実行

想定コマンド:

```powershell
dotnet test
```

PostgreSQLを使う結合テストはTestcontainers for .NETでPostgreSQLを起動する。

Docker ComposeのテストDBは、本番相当構成の確認、E2E、手動検証が必要な場合に使用する。

### 15.2 CI実行順

mainからIssue単位で作業ブランチを切り、PRで以下を実行する。mainへのマージ条件は、PR CI成功とレビュー完了とする。

1. restore
2. build
3. unit tests
4. integration tests
5. E2E smoke tests
6. publish artifact

PR CIのE2Eは最小セットのみ実行する。mainマージ後または夜間CIでは全E2Eを実行し、外部APIは実APIではなくモックまたはスタブを使用する。

## 16. テスト環境設定

### 16.1 環境変数

| 変数 | 用途 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT=Test` | テスト環境 |
| `ConnectionStrings__DefaultConnection` | テストDB |
| `ExternalApis__UseMocks=true` | 外部APIモック |
| `Seed__Enabled=true` | テストデータ投入 |

### 16.2 禁止事項

- 本番DBへ接続しない。
- 本番APIキーを使用しない。
- 実WordPressへ投稿しない。
- 実Discordチャンネルへ通知しない。
- 実Tavily API、実X APIを通常の自動テストで呼び出さない。
- `.env`や秘密情報をコミットしない。

## 17. 受け入れ基準

MVP実装完了時点で以下を満たす。

- 単体テストがすべて成功する。
- API結合テストがすべて成功する。
- DBマイグレーションがテストPostgreSQLへ適用できる。
- ジョブ取得、成功、失敗、再試行、キャンセルの結合テストが成功する。
- 外部APIモックによるAI生成、Tavily検索、X投稿検索、WordPress投稿、Discord通知のテストが成功する。
- X投稿検索で同じ投稿が重複保存されない。
- X投稿本文、投稿者名、プロフィール情報、メディアURLが最大24時間で削除または匿名化される。
- 生成済み記事内のX引用は公開前に再検証される。
- productionとstrictではX投稿の表示・公開前に必ず再取得される。
- strictではX投稿生データが1時間で期限切れになる。
- X API Full-Archive Searchは必要時のみ実行され、通常100件、大量調査時500件を超えない。
- X APIの月間安全上限10,000から50,000 postsを超える見込みの場合は停止または管理者確認になる。
- 環境単位TTLより緩い上書きが拒否または無効化される。
- ユーザー、記事、データソース、トピック単位の指定でTTLを厳しくできる。
- compliance_strict記事はstrict TTLになり、人間確認前の公開投稿が抑止される。
- legalFinanceHealth、politicsSafetyReputation一致時はcompliance_strictになる。
- freshness、newsTrend、pricing、productAvailability、comparisonReview、techSaaS、sourceSignals一致時はstrictになる。
- 辞書更新時はテストケースを追加し、判定結果を確認してから反映する。
- WordPress投稿ステータスの初期値が下書きである。
- 主要E2Eシナリオが成功する。
- 未認証、権限不足、他ユーザー記事アクセスが拒否される。
- 秘密情報がレスポンスとログに出ない。

## 18. 未確定事項

- 性能テストをCIへ組み込むか、リリース前手動にするか。
