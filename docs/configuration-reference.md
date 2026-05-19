# 設定リファレンス

## 1. 目的

本書は、AIライティングツールで使用する設定値、環境変数、Optionsセクション、秘密情報の扱いを定義する。

対象は、Blazor Web App、ASP.NET Core Minimal API、ASP.NET Core Identity、EF Core/PostgreSQL、BackgroundService、外部API連携、Docker Compose、Caddy、VPS運用である。

## 2. 基本方針

- 設定値はOptionsクラスへバインドする。
- Optionsクラス名と設定セクション名を一致させる。
- `appsettings*.json` には秘密情報を入れない。
- ローカル開発ではUser Secretsまたはローカル環境変数を使う。
- Docker ComposeとVPS本番では `.env` または権限制限されたsecret fileを使う。
- `.env`、実APIキー、DBパスワード、Webhook URL、Application PasswordをGit管理しない。
- WordPress Application PasswordとDiscord Webhook URLは環境変数ではなく、ユーザー別にDB暗号化保存する。
- 通常の自動テストでは外部本番APIを呼ばず、モックまたはスタブを使う。

## 3. 設定ソース

ASP.NET Core標準の設定読み込みを前提とする。後から読み込まれる設定ほど優先する。

| 優先度 | ソース | 用途 |
| --- | --- | --- |
| 低 | `appsettings.json` | 全環境共通の非秘密情報 |
| 中 | `appsettings.{Environment}.json` | 環境別の非秘密情報 |
| 中 | User Secrets | ローカル開発用の秘密情報 |
| 高 | 環境変数 | Docker Compose、本番、CI |
| 高 | コマンドライン引数 | 一時的な上書き |
| DB | アプリ内設定テーブル | ユーザー別WordPress設定、通知設定、利用上限、AIモデル設定 |

環境変数では `:` の代わりに `__` を使う。

例:

| 設定キー | 環境変数 |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `AiProviders:Gemini:ApiKey` | `AiProviders__Gemini__ApiKey` |
| `Security:DataProtectionKeysPath` | `Security__DataProtectionKeysPath` |

## 4. 環境

| 環境 | `ASPNETCORE_ENVIRONMENT` | 用途 |
| --- | --- | --- |
| local | `Development` | ローカル開発 |
| docker-local | `Development` または `Docker` | Docker Composeによる統合確認 |
| test | `Test` | 自動テスト |
| staging | `Staging` | 本番前確認 |
| production | `Production` | 本番 |

`Test` 環境では外部APIモックを既定とし、本番DBや本番APIキーを使用しない。

## 5. Optionsセクション一覧

| セクション | Options | 主な用途 |
| --- | --- | --- |
| `App` | `AppOptions` | 公開URL、アプリ名、リンク生成 |
| `ConnectionStrings` | なし | PostgreSQL接続文字列 |
| `AiProviders` | `AiProviderOptions` | AI Provider、モデル、APIキー、タイムアウト |
| `SearchProviders` | `SearchProviderOptions` | Tavily、X API、検索上限 |
| `SearchCache` | `SearchCacheOptions` | 検索キャッシュ環境ポリシー |
| `Wordpress` | `WordpressOptions` | WordPress APIタイムアウト、許可スキーム |
| `Notifications` | `NotificationOptions` | 通知Provider、送信タイムアウト |
| `BackgroundJobs` | `BackgroundJobOptions` | Worker間隔、ロック、試行回数 |
| `Security` | `SecurityOptions` | Data Protectionキー、Cookie、SSRF対策 |
| `UsageLimits` | `UsageLimitOptions` | 既定の利用上限 |
| `ExternalApis` | `ExternalApiOptions` | テスト用モック切り替え |
| `Seed` | `SeedOptions` | テストSeed切り替え |
| `AdminSeed` | `AdminSeedOptions` | 初期Admin作成 |

## 6. 必須環境変数

### 6.1 アプリ基本設定

| 環境変数 | 設定キー | local | production | 秘密情報 | 内容 |
| --- | --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | 必須 | 必須 | No | 実行環境 |
| `ASPNETCORE_URLS` | `ASPNETCORE_URLS` | 任意 | 必須 | No | appコンテナの待受URL。Dockerでは `http://+:8080` |
| `App__BaseUrl` | `App:BaseUrl` | 推奨 | 必須 | No | 公開URL。通知URL、リンク生成、Caddy配下の基準URL |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | 必須 | 必須 | Yes | PostgreSQL接続文字列 |

### 6.2 PostgreSQL / Docker Compose

| 環境変数 | local | production | 秘密情報 | 内容 |
| --- | --- | --- | --- | --- |
| `POSTGRES_DB` | Docker時必須 | Docker時必須 | No | PostgreSQL DB名 |
| `POSTGRES_USER` | Docker時必須 | Docker時必須 | No | PostgreSQLユーザー |
| `POSTGRES_PASSWORD` | Docker時必須 | Docker時必須 | Yes | PostgreSQLパスワード |

`POSTGRES_*` はPostgreSQLコンテナ初期化用である。アプリは `ConnectionStrings__DefaultConnection` を使ってDBへ接続する。

### 6.3 AI Provider

| 環境変数 | 設定キー | local | production | 秘密情報 | 既定値 / 方針 |
| --- | --- | --- | --- | --- | --- |
| `AiProviders__Gemini__ApiKey` | `AiProviders:Gemini:ApiKey` | AI実行時必須 | 必須 | Yes | Gemini APIキー |
| `AiProviders__Gemini__Model` | `AiProviders:Gemini:Model` | 推奨 | 必須 | No | `gemini-3.1-pro-preview` |
| `AiProviders__Gemini__Region` | `AiProviders:Gemini:Region` | 推奨 | 必須 | No | `Japan` |
| `AiProviders__Gemini__TimeoutSeconds` | `AiProviders:Gemini:TimeoutSeconds` | 任意 | 任意 | No | 120 |
| `AiProviders__Gemini__MaxInputChars` | `AiProviders:Gemini:MaxInputChars` | 任意 | 任意 | No | 実装時にモデル制限へ合わせる |

Gemini以外のAI ProviderはMVP対象外である。後続フェーズで追加する場合は `AiProviders:{ProviderName}:*` の形で拡張する。

### 6.4 検索Provider

| 環境変数 | 設定キー | local | production | 秘密情報 | 既定値 / 方針 |
| --- | --- | --- | --- | --- | --- |
| `SearchProviders__Tavily__ApiKey` | `SearchProviders:Tavily:ApiKey` | 検索時必須 | 必須 | Yes | Tavily APIキー |
| `SearchProviders__Tavily__Endpoint` | `SearchProviders:Tavily:Endpoint` | 任意 | 任意 | No | 公式Endpoint |
| `SearchProviders__Tavily__TimeoutSeconds` | `SearchProviders:Tavily:TimeoutSeconds` | 任意 | 任意 | No | 30 |
| `SearchProviders__X__BearerToken` | `SearchProviders:X:BearerToken` | X検索時必須 | 必須 | Yes | X API Bearer Token |
| `SearchProviders__X__Endpoint` | `SearchProviders:X:Endpoint` | 任意 | 任意 | No | 公式Endpoint |
| `SearchProviders__X__TimeoutSeconds` | `SearchProviders:X:TimeoutSeconds` | 任意 | 任意 | No | 30 |
| `SearchProviders__X__DefaultMaxResults` | `SearchProviders:X:DefaultMaxResults` | 任意 | 任意 | No | 100 |
| `SearchProviders__X__BulkMaxResults` | `SearchProviders:X:BulkMaxResults` | 任意 | 任意 | No | 500 |
| `SearchProviders__X__MonthlySafetyLimitPosts` | `SearchProviders:X:MonthlySafetyLimitPosts` | 任意 | 必須 | No | 10,000から50,000 posts程度で開始 |
| `SearchProviders__DefaultRegion` | `SearchProviders:DefaultRegion` | 任意 | 任意 | No | `Japan` |

X API Full-Archive SearchはPay-per-useを前提とし、必要時のみ実行する。

### 6.5 検索キャッシュ

| 環境変数 | 設定キー | local | production | 秘密情報 | 値 |
| --- | --- | --- | --- | --- | --- |
| `SearchCache__Policy` | `SearchCache:Policy` | 必須 | 必須 | No | `dev` / `staging` / `production` / `strict` |

環境別TTLの基準:

| Policy | Tavily検索結果JSON | Tavily本文・要約・スニペット | X投稿生データ | X表示・公開前 |
| --- | --- | --- | --- | --- |
| `dev` | 24時間 | 24時間 | 6時間 | 任意 |
| `staging` | 6時間 | 24時間 | 6時間 | 推奨 |
| `production` | 24時間 | 7日 | 24時間 | 必須 |
| `strict` | 24時間 | 24時間 | 1時間 | 必須 |

ユーザー、記事、データソース、トピック単位でTTL候補が複数ある場合は、最も短いTTLを採用する。

### 6.6 WordPress

| 環境変数 | 設定キー | local | production | 秘密情報 | 既定値 / 方針 |
| --- | --- | --- | --- | --- | --- |
| `Wordpress__TimeoutSeconds` | `Wordpress:TimeoutSeconds` | 任意 | 任意 | No | 60 |
| `Wordpress__RetryCount` | `Wordpress:RetryCount` | 任意 | 任意 | No | 実装時に確定 |
| `Wordpress__AllowedSchemes__0` | `Wordpress:AllowedSchemes:0` | 任意 | 任意 | No | `https` |

WordPressサイトURL、ログインID、Application Password、既定カテゴリ、サイト別ライティング設定はDBに保存する。Application Passwordは暗号化保存し、環境変数には置かない。

### 6.7 通知

| 環境変数 | 設定キー | local | production | 秘密情報 | 既定値 / 方針 |
| --- | --- | --- | --- | --- | --- |
| `Notifications__Provider` | `Notifications:Provider` | 任意 | 任意 | No | `Discord` |
| `Notifications__TimeoutSeconds` | `Notifications:TimeoutSeconds` | 任意 | 任意 | No | 30 |

Discord Webhook URLはユーザー別にDB暗号化保存する。環境変数による全体共有Webhookは使用しない。

### 6.8 BackgroundService

| 環境変数 | 設定キー | local | production | 秘密情報 | 既定値 |
| --- | --- | --- | --- | --- | --- |
| `BackgroundJobs__IdleDelaySeconds` | `BackgroundJobs:IdleDelaySeconds` | 任意 | 任意 | No | 3 |
| `BackgroundJobs__LockTimeoutMinutes` | `BackgroundJobs:LockTimeoutMinutes` | 任意 | 任意 | No | 30 |
| `BackgroundJobs__MaxJobsPerLoop` | `BackgroundJobs:MaxJobsPerLoop` | 任意 | 任意 | No | 1 |
| `BackgroundJobs__DefaultMaxAttempts` | `BackgroundJobs:DefaultMaxAttempts` | 任意 | 任意 | No | 3 |
| `BackgroundJobs__WorkerIdPrefix` | `BackgroundJobs:WorkerIdPrefix` | 任意 | 任意 | No | `app` |
| `BackgroundJobs__SearchCacheCleanupIntervalMinutes` | `BackgroundJobs:SearchCacheCleanupIntervalMinutes` | 任意 | 任意 | No | 60 |

ジョブ種別ごとの上書きが必要になった場合は、`BackgroundJobs:JobTypes:{JobType}:MaxAttempts` のような階層で追加する。

### 6.9 Security / Data Protection

| 環境変数 | 設定キー | local | production | 秘密情報 | 内容 |
| --- | --- | --- | --- | --- | --- |
| `Security__DataProtectionKeysPath` | `Security:DataProtectionKeysPath` | 任意 | 必須 | No | Data Protectionキー保存先 |
| `Security__RequireHttps` | `Security:RequireHttps` | 任意 | 必須 | No | HTTPS必須化 |
| `Security__AllowedForwardedHosts__0` | `Security:AllowedForwardedHosts:0` | 任意 | 推奨 | No | Forwarded Headersで許可するホスト |
| `Security__CookieSecurePolicy` | `Security:CookieSecurePolicy` | 任意 | 必須 | No | `Always` |

Data ProtectionキーはCookie認証と暗号化保存に影響する。productionでは永続volumeへ保存し、バックアップ対象にする。

### 6.10 Admin Seed

| 環境変数 | 設定キー | local | production | 秘密情報 | 内容 |
| --- | --- | --- | --- | --- | --- |
| `AdminSeed__Email` | `AdminSeed:Email` | 初回のみ | 初回のみ | No | 初期Adminメール |
| `AdminSeed__Password` | `AdminSeed:Password` | 初回のみ | 初回のみ | Yes | 初期Adminパスワード |

初期Admin Seedは、Adminユーザーが存在しない場合のみ作成する。既存Adminのパスワードを起動時に上書きしない。初回ログイン後は初期Adminパスワードを変更し、`.env` またはsecret fileから削除または無効化する。

### 6.11 テスト専用

| 環境変数 | 設定キー | 必須 | 秘密情報 | 内容 |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT=Test` | `ASPNETCORE_ENVIRONMENT` | 必須 | No | テスト環境 |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | 必須 | Yes | テストDB |
| `ExternalApis__UseMocks` | `ExternalApis:UseMocks` | 必須 | No | 外部APIモック使用 |
| `Seed__Enabled` | `Seed:Enabled` | 必須 | No | テストデータ投入 |

テストでは本番DB、本番APIキー、実WordPress、実Discordを使用しない。

## 7. DB保存の設定

以下は環境変数ではなくDBに保存する。

| 設定 | 保存先 | 秘密情報 | 方針 |
| --- | --- | --- | --- |
| WordPressサイトURL | `WordpressSites.BaseUrl` | No | HTTPSのみ許可 |
| WordPressログインID | `WordpressSites.LoginId` | 一部秘密 | レスポンス出力は必要最小限 |
| WordPress Application Password | `WordpressSites.EncryptedApplicationPassword` | Yes | 暗号化保存、レスポンス除外 |
| WordPress既定カテゴリ | `WordpressSites.DefaultCategoryId` | No | 投稿時の既定カテゴリ |
| サイト別ライティング設定 | `WordpressSites.*Profile*` | No | 記事作成時にスナップショット保存 |
| Discord Webhook URL | `NotificationSettings.EncryptedWebhookUrl` | Yes | 暗号化保存、平文レスポンス禁止 |
| Discord Webhookマスク表示 | `NotificationSettings.DestinationMasked` | No | 画面表示用 |
| AIモデル設定 | `AiModelSettings` | No | 利用可能モデルの管理 |
| ユーザー利用上限 | `UserUsageLimits` | No | 管理者が更新 |

## 8. `.env.example`

`.env.example` にはキーとダミー値のみを置く。

```dotenv
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
App__BaseUrl=https://example.com

POSTGRES_DB=web_writing_tool
POSTGRES_USER=web_writing_tool
POSTGRES_PASSWORD=change-me

ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=web_writing_tool;Username=web_writing_tool;Password=change-me

AiProviders__Gemini__ApiKey=change-me
AiProviders__Gemini__Model=gemini-3.1-pro-preview
AiProviders__Gemini__Region=Japan
AiProviders__Gemini__TimeoutSeconds=120

SearchProviders__Tavily__ApiKey=change-me
SearchProviders__Tavily__TimeoutSeconds=30
SearchProviders__X__BearerToken=change-me
SearchProviders__X__TimeoutSeconds=30
SearchProviders__X__DefaultMaxResults=100
SearchProviders__X__BulkMaxResults=500
SearchCache__Policy=production

Wordpress__TimeoutSeconds=60
Notifications__Provider=Discord
Notifications__TimeoutSeconds=30

BackgroundJobs__IdleDelaySeconds=3
BackgroundJobs__LockTimeoutMinutes=30
BackgroundJobs__MaxJobsPerLoop=1
BackgroundJobs__DefaultMaxAttempts=3
BackgroundJobs__WorkerIdPrefix=app

Security__DataProtectionKeysPath=/var/app/keys
Security__RequireHttps=true
Security__CookieSecurePolicy=Always

AdminSeed__Email=admin@example.com
AdminSeed__Password=change-me
```

実値を含む `.env` はGit管理しない。

## 9. `appsettings.json` に置いてよい値

| 種別 | 例 |
| --- | --- |
| UI既定値 | ページサイズ、一覧表示件数 |
| ジョブ既定値 | ポーリング間隔、最大試行回数 |
| 外部API非秘密値 | モデルID、タイムアウト、既定リージョン |
| キャッシュ方針 | TTL既定値、ポリシー名 |
| ログ設定 | ログレベル、構造化ログ設定 |
| セキュリティ非秘密値 | Cookie方針、許可スキーム |

`appsettings*.json` にDBパスワード、APIキー、Bearer Token、Webhook URL、Application Password、初期Adminパスワードを入れない。

## 10. User Secrets例

Webプロジェクト作成後、`src/WebWritingTool.Web` で設定する。

```powershell
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=web_writing_tool;Username=web_writing_tool;Password=change-me"
dotnet user-secrets set "AiProviders:Gemini:ApiKey" "<your-gemini-api-key>"
dotnet user-secrets set "AiProviders:Gemini:Model" "gemini-3.1-pro-preview"
dotnet user-secrets set "AiProviders:Gemini:Region" "Japan"
dotnet user-secrets set "SearchProviders:Tavily:ApiKey" "<your-tavily-api-key>"
dotnet user-secrets set "SearchProviders:X:BearerToken" "<your-x-bearer-token>"
dotnet user-secrets set "SearchCache:Policy" "dev"
dotnet user-secrets set "AdminSeed:Email" "admin@example.com"
dotnet user-secrets set "AdminSeed:Password" "<local-admin-password>"
```

実APIを使わない段階では外部APIキー未設定でも起動できるようにする。外部連携機能を実行した場合だけ未設定エラーにする。

## 11. 検証ルール

起動時に検証する項目:

- `ConnectionStrings:DefaultConnection` が設定されている。
- `ASPNETCORE_ENVIRONMENT=Production` では `Security:DataProtectionKeysPath` が設定されている。
- `SearchCache:Policy` が許可値に含まれる。
- `AiProviders:Gemini:Model` が空ではない。
- `Wordpress:AllowedSchemes` は `https` のみを許可する。
- `AdminSeed:Password` はSeed実行時のみ必須とし、ログに出さない。

機能実行時に検証する項目:

- Gemini生成時に `AiProviders:Gemini:ApiKey` が設定されている。
- Tavily検索時に `SearchProviders:Tavily:ApiKey` が設定されている。
- X検索時に `SearchProviders:X:BearerToken` が設定されている。
- WordPress投稿時に対象サイトのApplication Passwordを復号できる。
- Discord通知時に対象ユーザーのWebhook URLを復号できる。

## 12. ログ禁止値

以下をログ、レスポンス、監査ログ、ジョブPayload、ジョブResultへ出さない。

- DB接続文字列のパスワード部分
- Gemini API Key
- Tavily API Key
- X API Bearer Token
- WordPress Application Password
- Discord Webhook URL
- Cookie
- Authorizationヘッダー
- Data Protectionキー
- 初期Adminパスワード
- プロンプト全文
- 記事本文全文

ログには、外部APIのHTTPステータス、共通エラーコード、ジョブID、処理時間など、切り分けに必要な最小情報だけを残す。

## 13. 変更時の更新対象

設定値を追加・変更した場合は、以下を同時に確認する。

- Optionsクラス
- `Program.cs` の設定バインド
- `appsettings*.json`
- `.env.example`
- Docker Compose
- CI設定
- [docs/environment-setup.md](environment-setup.md)
- [docs/operation-design.md](operation-design.md)
- [docs/security-design.md](security-design.md)
- 本書
