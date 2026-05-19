# コーディング規約・実装ガイドライン

## 1. 目的

本書は、AIライティングツールの実装品質を安定させるためのコーディング規約と実装判断基準を定義する。
Codexで実装を進める場合も、本書と`todo.md`を基準にする。

対象:

- Blazor Web App
- ASP.NET Core Minimal API
- ASP.NET Core Identity
- EF Core / PostgreSQL
- BackgroundService
- 外部API Client
- Docker Compose / Caddy / VPS運用
- 単体テスト、結合テスト、E2Eテスト

## 2. 基本方針

- 既存設計書を正とする。
- 実装前に関連する`docs/*.md`と`todo.md`を確認する。
- 小さく実装し、小さく確認する。
- UI、Application、Infrastructure、DBの責務を混ぜない。
- 認証、認可、入力検証、秘密情報保護は後回しにしない。
- 外部APIは直接呼び出さず、必ずClientインターフェース越しに扱う。
- テストで実外部APIを通常呼び出さない。
- 破壊的なMigrationは事前に設計書と影響範囲を確認する。

## 3. Solution構成

実装時の基本構成:

```text
src/
  WebWritingTool.Web/
  WebWritingTool.Application/
  WebWritingTool.Domain/
  WebWritingTool.Infrastructure/
tests/
  WebWritingTool.UnitTests/
  WebWritingTool.IntegrationTests/
  WebWritingTool.E2ETests/
```

| プロジェクト | 責務 |
| --- | --- |
| `WebWritingTool.Web` | Blazor画面、Minimal API、認証、認可、DI起点、画面状態 |
| `WebWritingTool.Application` | ユースケース、入力検証、DTO、トランザクション境界、業務ルール |
| `WebWritingTool.Domain` | エンティティ、値オブジェクト、列挙型、業務ルール |
| `WebWritingTool.Infrastructure` | EF Core、外部API Client、暗号化、ファイル保存、ジョブ実行補助 |
| `WebWritingTool.UnitTests` | Domain / Application / Infrastructure単体テスト |
| `WebWritingTool.IntegrationTests` | API、DB、BackgroundService、外部Clientモック結合テスト |
| `WebWritingTool.E2ETests` | Playwrightによる主要画面E2Eテスト |

依存方向:

```text
Web -> Application
Web -> Infrastructure
Application -> Domain
Infrastructure -> Application
Infrastructure -> Domain
```

Application層はWeb層とInfrastructure層へ依存しない。外部連携やDBの実装はInfrastructure層に置き、Application層はインターフェースとDTOに依存する。
Web層から`DbContext`を直接操作しない。

## 4. 命名規則

### 4.1 C#

| 対象 | 規則 | 例 |
| --- | --- | --- |
| クラス | PascalCase | `ArticleCommandService` |
| インターフェース | `I` + PascalCase | `IArticleCommandService` |
| メソッド | PascalCase | `CreateArticleAsync` |
| 変数 | camelCase | `articleId` |
| private field | `_camelCase` | `_dbContextFactory` |
| 定数 | PascalCase | `DefaultPageSize` |
| 非同期メソッド | `Async` suffix | `SendAsync` |
| CancellationToken | 最終引数 | `CancellationToken cancellationToken` |

### 4.2 DTO

| 用途 | suffix | 例 |
| --- | --- | --- |
| API Request | `Request` | `CreateArticleRequest` |
| API Response | `Response` | `ArticleDetailResponse` |
| Application入力 | `Command` / `Query` | `CreateArticleCommand` |
| Application結果 | `Result` | `CreateArticleResult` |
| 外部API DTO | Provider名を含める | `GeminiGenerateRequest` |

DB EntityをAPIレスポンスとして直接返さない。

### 4.3 Options

Optionsクラスは設定セクションと一致させる。

例:

- `AiProviderOptions`
- `SearchProviderOptions`
- `WordpressOptions`
- `NotificationOptions`
- `BackgroundJobOptions`
- `SecurityOptions`

Options値、特にAPIキーやWebhook URLをログに出さない。

## 5. Blazor実装ルール

- 画面仕様は`docs/screen-design.md`を優先する。
- 画面はApplicationサービスまたはMinimal APIを呼ぶ。
- 画面内にDBアクセス、外部API呼び出し、複雑な業務ルールを書かない。
- フォーム入力はViewModelを使う。
- サーバー側検証を必須とし、クライアント側検証だけに依存しない。
- 認可はUI表示制御だけで完結させない。
- 生成本文や外部データをHTML表示する場合はサニタイズ済みHTMLだけを描画する。
- 長時間処理は画面から直接待たず、ジョブ登録後に状態表示する。

コンポーネント配置例:

```text
Components/
  Layout/
  Articles/
  Settings/
  Admin/
Pages/
  Articles/
  Admin/
```

## 6. Minimal API実装ルール

- API仕様は`docs/api-design.md`を正とする。
- エンドポイントは機能単位で`MapGroup`する。
- 管理者APIは`RequireAuthorization("RequireAdmin")`を適用する。
- 認証必須APIは`RequireAuthorization()`を適用する。
- 所有者確認はAPI属性だけで完結させず、Applicationサービスでも確認する。
- 入力不正は`ProblemDetails`で返す。
- 秘密情報をレスポンスに含めない。
- APIはEntityを直接受け取らない、返さない。
- OpenAPI向けに`WithName`、`WithSummary`、`WithDescription`を設定する。

レスポンス方針:

| 状態 | レスポンス |
| --- | --- |
| 作成成功 | `201 Created` |
| ジョブ登録成功 | `202 Accepted` |
| 更新成功 | `200 OK`または`204 No Content` |
| 削除成功 | `204 No Content` |
| 入力不正 | `400 Bad Request` + ProblemDetails |
| 未認証 | `401 Unauthorized` |
| 権限不足 | `403 Forbidden` |
| 見つからない | `404 Not Found` |
| 競合 | `409 Conflict` |
| 利用上限 | `422 Unprocessable Entity` |

## 7. Application層ルール

Application層はユースケース単位で実装する。

例:

- `IArticleCommandService`
- `IArticleQueryService`
- `IHeadingService`
- `IJobQueueService`
- `IUsageLimitService`
- `IReferenceResearchService`
- `IWordpressSiteService`
- `IWordpressPostService`
- `INotificationSettingService`
- `IAdminUserService`

Application層で行うこと:

- 認可に必要な所有者確認
- 入力検証
- 業務ルール
- トランザクション境界
- EntityとDTOの変換
- ジョブ登録
- 監査ログ登録

Application層で行わないこと:

- HTTP Request / Responseの直接操作
- Blazor UI状態管理
- Provider固有APIリクエストの組み立て
- SQL文字列の直書き

## 8. EF Core / DBルール

### 8.1 DbContext

- `ApplicationDbContext`はIdentity用DbContextを継承する。
- IdentityユーザーIDは`string`を許容する。
- 業務テーブルの主キーは原則`Guid`。
- 日時はUTCで保存する。
- enumは`varchar`として保存する。
- PostgreSQL固有型は設計書に明記して使う。

### 8.2 Query

- EF Core LINQを基本とする。
- Raw SQLは原則避ける。
- Raw SQLが必要な場合は必ずパラメータ化する。
- ユーザー入力をSQL文字列へ連結しない。
- 一覧取得はページング必須。
- 大きな本文やJSONは必要なときだけ取得する。

### 8.3 論理削除

- 通常の記事削除、WordPressサイト削除、通知設定削除は`DeletedAt`による論理削除を基本とする。
- 通常検索では論理削除済みを除外する。
- 本人退会と管理者によるユーザー削除だけは例外的に物理削除する。
- ユーザー物理削除はApplicationサービスで明示的に順序制御し、DBカスケードだけに依存しない。

### 8.4 Migration

- MigrationはEF Core Migrationsで管理する。
- Migration作成前に`docs/db-design.md`との差分を確認する。
- 本番適用前にSQLスクリプトを確認する。
- カラム削除、型変更、NOT NULL化などの破壊的変更は段階的に行う。
- 本番Migration前にDBバックアップを取得する。

## 9. Identity / 認可ルール

- 認証はASP.NET Core Identityを使う。
- Admin / Userロールを使う。
- Admin機能は必ずAdminポリシーを要求する。
- ユーザー別データは必ず`UserId`で分離する。
- BackgroundServiceでも処理直前に所有者整合性を再確認する。
- UIのボタン非表示を認可の代替にしない。

Adminユーザー追加:

- 初期Adminは起動時Seedで作成する。
- Seedは`UserManager` / `RoleManager`を使う。
- 既にAdminが存在する場合、Seedは新規作成もパスワード上書きもしない。
- 2人目以降のAdminは管理画面から既存ユーザーを昇格する、または管理者が新規ユーザー作成時にAdminロールを付与する。
- 緊急復旧時のみCLIまたは一時SeedコマンドでAdminを復旧できる設計にする。
- DBへ直接`AspNetUserRoles`をINSERTしない。
- 固定Adminパスワードをコードへ埋め込まない。

Adminユーザー削除:

- Adminは他ユーザーを削除できる。
- 管理者自身は削除できない。
- 最後のAdminユーザーは削除できない。
- 最後のAdminユーザーは降格、無効化できない。
- 対象ユーザーに`Running`ジョブがある場合は削除できない。
- 対象ユーザーに紐づく業務データと、対象ユーザーが操作した既存監査ログもトランザクション内で物理削除する。
- 監査ログには削除対象ユーザーIDを文字列スナップショットとして残し、削除対象ユーザーへのFKは持たない。

本人退会:

- 本人退会はログインユーザー本人のみ対象にする。
- 現在パスワードの再確認を必須にする。
- 最後のAdminユーザーは退会できない。
- 対象ユーザーに`Running`ジョブがある場合は退会できない。
- 対象ユーザーに紐づく業務データと、対象ユーザーが操作した既存監査ログもトランザクション内で物理削除する。
- 退会監査ログは削除対象ユーザーへのFKを持たず、対象ユーザーIDを文字列スナップショットとして残す。

## 10. BackgroundService / ジョブルール

- ジョブ仕様は`docs/job-design.md`を正とする。
- `BackgroundService`にScoped DbContextを直接注入しない。
- `IDbContextFactory<ApplicationDbContext>`または`IServiceScopeFactory`を使う。
- ジョブ取得とRunning更新はトランザクションで行う。
- 同一ジョブの多重実行を防ぐ。
- 外部APIの実行は原則ジョブ内で行う。
- JobPayloadに秘密情報を入れない。
- サイト別ライティング設定の本文は`Articles.WritingProfileSnapshotJson`から読み、JobPayloadへ重複保存しない。
- エラーには秘密情報、プロンプト全文、本文全文、外部APIレスポンス全文を含めない。
- リトライ可否はエラー種別で判定する。
- 副作用のあるジョブは冪等性を持たせる。

ジョブHandler命名:

```text
IJobHandler
TitleGenerationJobHandler
OutlineGenerationJobHandler
BodyGenerationJobHandler
WebSearchJobHandler
XFullArchiveSearchJobHandler
WordpressPostJobHandler
NotificationJobHandler
```

## 11. 外部API Clientルール

- 外部連携仕様は`docs/external-integration-design.md`を正とする。
- `IHttpClientFactory`を使う。
- Provider固有DTOはInfrastructure層に閉じ込める。
- Application層へは共通DTOまたはResultで返す。
- APIキーはOptionsから読み、ログに出さない。
- タイムアウト、リトライ、レート制限を設定する。
- 429、401、Timeout、5xxを共通エラーへ変換する。
- 実APIは通常テストで呼ばない。

対象Client:

- `GeminiTextGenerationClient`
- `TavilyWebSearchClient`
- `XFullArchiveSearchClient`
- `WordpressClient`
- `DiscordNotificationClient`

## 12. キャッシュ / strict判定ルール

- Tavily / Xキャッシュ仕様は`docs/external-integration-design.md`と`docs/security-design.md`を正とする。
- 検索条件は正規化し、`QueryHash`でキャッシュ判定する。
- TTLは環境、ユーザー、記事、データソース、トピックのうち最も短い値を採用する。
- `production`と`strict`では、X投稿の表示・公開前に必ず再取得する。
- `legalFinanceHealth`と`politicsSafetyReputation`は`compliance_strict`にする。
- `compliance_strict`は`strict + HumanReviewRequired`として扱う。
- `HumanReviewRequired = true`かつ`HumanReviewedAt`未設定のWordPress Publishは拒否する。Draftは許可する。
- 一括登録からのWordPress自動投稿はDraft固定とし、同一記事の自動投稿ジョブを二重登録しない。

## 13. 秘密情報ルール

出してはいけないもの:

- Gemini API Key
- Tavily API Key
- X API Bearer Token
- DBパスワード
- WordPress Application Password
- Discord Webhook URL
- 認証Cookie
- CSRF Token
- Authorizationヘッダー
- プロンプト全文
- 記事本文全文

ルール:

- appsettingsに秘密情報を入れない。
- `.env`をGit管理しない。
- User Secrets、環境変数、またはGit管理外の`.env`を使う。
- 開発用.NET SDKコンテナでUser Secretsを使う場合は、保存先を永続化する。
- WordPress Application PasswordとDiscord Webhook URLはDB暗号化保存する。
- レスポンスDTOに秘密情報を含めない。
- 例外メッセージに秘密情報を含めない。
- ログ出力前にマスクする。

## 14. ログ / 監査ログルール

### 14.1 アプリログ

出してよい情報:

- RequestId
- UserId
- ArticleId
- JobId
- 外部連携種別
- ステータス
- 処理時間
- エラーコード

出してはいけない情報は秘密情報ルールに従う。

### 14.2 監査ログ

監査ログ対象:

- ログイン成功/失敗
- WordPressサイト登録/更新/削除
- Discord通知設定更新
- WordPress投稿
- Adminによるユーザー削除
- 本人退会
- AIモデル設定変更
- strict辞書更新
- 人間確認完了
- ジョブキャンセル

監査ログにも秘密情報を保存しない。

## 15. 例外 / ProblemDetailsルール

- API境界ではProblemDetails形式へ変換する。
- 内部例外のスタックトレースを本番レスポンスに出さない。
- 外部APIのエラー本文全文を返さない。
- ユーザー向けメッセージと内部ログを分ける。
- 業務エラーはエラーコードを持たせる。

例:

| ErrorCode | 用途 |
| --- | --- |
| `ValidationFailed` | 入力不正 |
| `UnauthorizedExternalApi` | 外部API認証失敗 |
| `RateLimited` | レート制限 |
| `ExternalTimeout` | 外部APIタイムアウト |
| `UsageLimitExceeded` | 利用上限超過 |
| `ConflictRunningJob` | Runningジョブ競合 |
| `HumanReviewRequired` | 人間確認が必要 |

## 16. テストルール

テスト方針は`docs/test-design.md`を正とする。

- テストはxUnitを基本とする。
- PostgreSQL依存の結合テストではEF Core InMemory Providerを使わない。
- 外部API Clientはモックまたはテストダブルに差し替える。
- 認証/認可テストではテスト認証Handlerを使う。
- 未認証、権限不足、他ユーザーアクセスは必ず検証する。
- 秘密情報がレスポンスとログに出ないことを検証する。
- ユーザー物理削除はDB結合テストで検証する。
- E2Eは主要導線に絞る。

テスト命名:

```text
MethodName_State_ExpectedResult
```

例:

```text
DeleteUserAsync_TargetHasRunningJob_ReturnsConflict
CreateWordpressSiteAsync_ContainsApplicationPassword_DoesNotExposeSecret
GenerateBodyAsync_WithWritingProfile_AppliesSitePersona
```

## 17. コメントルール

- 自明な処理にはコメントを書かない。
- 業務上の制約、セキュリティ理由、外部API仕様差分はコメントしてよい。
- コメントは実装と乖離しやすいため、設計判断は可能な限り設計書へ残す。

良い例:

```csharp
// X raw data must expire quickly because deleted or private posts must not be reused.
```

悪い例:

```csharp
// userIdを代入する
```

## 18. フォーマット

- `scripts/format.ps1`を基本とし、開発用.NET SDKコンテナ経由で`dotnet format`を実行する。
- `.editorconfig`を導入する。
- usingは不要なものを削除する。
- nullable reference typesを有効にする。
- warningを無視しない。
- 生成ファイル以外の広範囲な自動整形は避ける。

## 19. `todo.md`運用

- 実装は`todo.md`のタスクID単位で進める。
- タスク開始前に関連設計書を読む。
- タスク完了時にチェックを`[x]`へ更新する。
- 完了条件を満たしていない場合はチェックしない。
- 仕様差分が出た場合は、実装だけでなく関連`docs/*.md`も更新する。
- 1つのタスクで複数フェーズにまたがる変更を避ける。

## 20. レビュー観点

実装後は以下を確認する。

- 設計書と実装が一致しているか。
- 認証、認可、所有者チェックが抜けていないか。
- 秘密情報がレスポンス、ログ、DB平文に出ていないか。
- 外部APIの失敗、429、Timeoutを扱えるか。
- ジョブが多重実行されないか。
- DBクエリがページングされているか。
- Runningジョブがある削除操作を拒否できるか。
- 記事削除時に関連するQueuedジョブをCanceledへ更新できるか。
- X投稿のTTLと再取得ルールが守られているか。
- WordPress投稿の既定がDraftになっているか。
- `compliance_strict`でPublishが抑止されるか。
- テストが最小範囲で通っているか。

## 21. 禁止事項

- DB EntityをAPIレスポンスとして直接返す。
- UIだけで認可を済ませる。
- Web層から`DbContext`を直接操作する。
- `BackgroundService`へScoped DbContextを直接注入する。
- APIキー、Webhook URL、Application Passwordをログへ出す。
- 実外部APIを通常の自動テストで呼ぶ。
- X投稿本文をTTL超過後も保持する。
- WordPress投稿の既定をPublishにする。
- `HumanReviewRequired`を無視してPublishする。
- 破壊的Migrationを無確認で作る。
- ユーザー物理削除をDBカスケード任せにする。
