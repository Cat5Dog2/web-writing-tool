# 環境構築手順書

## 1. 目的

本書は、AIライティングツールをローカル開発環境、Docker Compose環境、VPS本番環境で構築するための手順を定義する。

対象技術:

- Blazor Web App
- ASP.NET Core
- ASP.NET Core Identity
- EF Core
- PostgreSQL
- BackgroundService
- Docker Compose
- Caddy
- VPS

## 2. 前提

### 2.1 対象環境

| 環境 | 用途 | 想定 |
| --- | --- | --- |
| local | 開発 | Windows + PowerShell、またはWSL2 |
| docker-local | ローカル統合確認 | Docker Composeでapp、postgres、caddyを起動 |
| staging | 本番前確認 | VPSまたは本番相当Docker Compose |
| production | 本番 | Linux VPS + Docker Compose + Caddy |

### 2.2 .NETバージョン

新規実装では.NET 10 / ASP.NET Core 10を基本とする。
実装時に別バージョンへ固定する場合は、`global.json`、Dockerfile、CI、READMEを同時に更新する。

### 2.3 リポジトリ前提

現時点では設計書と`todo.md`が先行している。
実装開始後は以下の構成を想定する。

```text
web-writing-tool/
  src/
    WebWritingTool.Web/
    WebWritingTool.Application/
    WebWritingTool.Domain/
    WebWritingTool.Infrastructure/
  tests/
    WebWritingTool.UnitTests/
    WebWritingTool.IntegrationTests/
    WebWritingTool.E2ETests/
  docs/
  scripts/
  docker-compose.yml
  Dockerfile
  Caddyfile
  todo.md
```

## 3. 必要なツール

### 3.1 ローカル開発

| ツール | 用途 |
| --- | --- |
| .NET SDK 10.x | ASP.NET Core / EF Core / テスト実行 |
| Docker Desktop | PostgreSQL、Docker Compose確認 |
| Git | ソース管理 |
| PowerShell 7以上推奨 | スクリプト実行 |
| Node.js | 必要になった場合のフロントエンド補助。MVP必須ではない |

確認コマンド:

```powershell
dotnet --info
docker --version
docker compose version
git --version
pwsh --version
```

PowerShell 5.1でも基本操作は可能だが、スクリプトはPowerShell 7での実行を推奨する。

### 3.2 VPS本番

| ツール | 用途 |
| --- | --- |
| Ubuntu LTS系 | 実行OS |
| Docker Engine | コンテナ実行 |
| Docker Compose Plugin | 複数サービス管理 |
| ufw | ファイアウォール |
| openssh-server | SSH接続 |
| rsyncまたはgit | デプロイ |

VPS要件の目安:

| 項目 | 最小 | 推奨 |
| --- | --- | --- |
| CPU | 2 vCPU | 2 vCPU以上 |
| メモリ | 2GB | 4GB以上 |
| ストレージ | 40GB | 80GB以上 |
| swap | 1GB | 2GB |

## 4. 秘密情報管理

### 4.1 基本方針

- 実APIキー、DBパスワード、Webhook URL、Application PasswordをGit管理しない。
- ローカル開発ではUser Secretsまたはローカル環境変数を使う。
- Docker Composeでは`.env`を使えるが、`.env`はGit管理しない。
- `.env.example`にはダミー値のみ置く。
- WordPress Application PasswordとDiscord Webhook URLはアプリ上ではDB暗号化保存する。
- Gemini、Tavily、X APIキーはDBへ保存しない。

### 4.2 必要な環境変数

| 変数 | 用途 | local | production |
| --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | 実行環境 | `Development` | `Production` |
| `ConnectionStrings__DefaultConnection` | PostgreSQL接続文字列 | 必須 | 必須 |
| `POSTGRES_DB` | PostgreSQL DB名 | Docker時必須 | Docker時必須 |
| `POSTGRES_USER` | PostgreSQLユーザー | Docker時必須 | Docker時必須 |
| `POSTGRES_PASSWORD` | PostgreSQLパスワード | Docker時必須 | Docker時必須 |
| `AiProviders__Gemini__ApiKey` | Gemini APIキー | AI実行時必須 | 必須 |
| `AiProviders__Gemini__Model` | GeminiモデルID | `gemini-3.1-pro-preview` | `gemini-3.1-pro-preview` |
| `AiProviders__Gemini__Region` | Gemini利用リージョン | `Japan` | `Japan` |
| `SearchProviders__Tavily__ApiKey` | Tavily APIキー | 検索時必須 | 必須 |
| `SearchProviders__X__BearerToken` | X API Bearer Token | X検索時必須 | 必須 |
| `SearchCache__Policy` | キャッシュ方針 | `dev` | `production` |
| `Security__DataProtectionKeysPath` | Data Protectionキー保存先 | 任意 | 必須 |
| `App__BaseUrl` | アプリURL | `https://localhost:xxxx` | 本番URL |
| `AdminSeed__Email` | 初期Adminメール | 初回のみ | 初回のみ |
| `AdminSeed__Password` | 初期Adminパスワード | 初回のみ | 初回のみ |

### 4.3 `.env.example`

`.env.example`には以下のようなキーだけを置く。
実値は入れない。

```dotenv
ASPNETCORE_ENVIRONMENT=Production

POSTGRES_DB=web_writing_tool
POSTGRES_USER=web_writing_tool
POSTGRES_PASSWORD=change-me

ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=web_writing_tool;Username=web_writing_tool;Password=change-me

AiProviders__Gemini__ApiKey=change-me
AiProviders__Gemini__Model=gemini-3.1-pro-preview
AiProviders__Gemini__Region=Japan

SearchProviders__Tavily__ApiKey=change-me
SearchProviders__X__BearerToken=change-me
SearchCache__Policy=production

Security__DataProtectionKeysPath=/var/app/keys
App__BaseUrl=https://example.com

AdminSeed__Email=admin@example.com
AdminSeed__Password=change-me
```

## 5. ローカル開発環境

### 5.1 初回取得

```powershell
git clone <repository-url>
cd web-writing-tool
```

### 5.2 .NET SDK確認

```powershell
dotnet --info
```

`global.json`がある場合は、表示されるSDKバージョンが一致していることを確認する。

### 5.3 User Secrets設定

Webプロジェクト作成後、Webプロジェクトディレクトリで実行する。

```powershell
cd src/WebWritingTool.Web
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

実APIを使わない段階では、外部APIキーは未設定でも起動できるように実装する。
外部連携機能を実行した場合だけ、未設定エラーにする。

### 5.4 PostgreSQL起動

ローカル開発ではPostgreSQLのみDockerで起動する構成を推奨する。

```powershell
docker compose up -d postgres
docker compose ps
```

実装前で`docker-compose.yml`がない場合は、P12実装後にこの手順を有効化する。

### 5.5 Migration適用

初期Migration作成後に実行する。

```powershell
dotnet ef database update --project src/WebWritingTool.Infrastructure --startup-project src/WebWritingTool.Web
```

EF Core CLIが未導入の場合:

```powershell
dotnet tool install --global dotnet-ef
dotnet ef --version
```

### 5.6 アプリ起動

```powershell
dotnet run --project src/WebWritingTool.Web
```

起動後に確認する。

- ログイン画面が表示される。
- 初期Adminでログインできる。
- `/health/live`が成功する。
- PostgreSQL接続が必要な画面でエラーにならない。

### 5.7 開発用HTTPS証明書

ローカルでHTTPSを使う場合:

```powershell
dotnet dev-certs https --trust
```

Cookie Secure、認証リダイレクト、CSRF確認のため、可能な限りHTTPSで開発する。

## 6. Docker Composeローカル環境

### 6.1 目的

Docker Composeローカル環境では、本番に近い形で以下を起動する。

- `app`
- `postgres`
- `caddy`

### 6.2 起動

```powershell
docker compose up -d --build
docker compose ps
```

### 6.3 ログ確認

```powershell
docker compose logs -f app
docker compose logs -f postgres
docker compose logs -f caddy
```

### 6.4 停止

```powershell
docker compose down
```

DBデータを消す場合のみvolume削除を行う。
通常は実行しない。

```powershell
docker compose down -v
```

### 6.5 永続volume

| Volume | 用途 |
| --- | --- |
| `postgres_data` | PostgreSQLデータ |
| `app_keys` | ASP.NET Core Data Protectionキー |
| `app_storage` | MVPでは未使用。後続フェーズのファイル保存用 |
| `caddy_data` | Caddy証明書、ACME情報 |
| `caddy_config` | Caddy設定 |

## 7. 本番VPS構築

### 7.1 OS初期設定

Ubuntu LTS系を想定する。

```bash
sudo apt update
sudo apt upgrade -y
sudo timedatectl set-timezone Asia/Tokyo
```

### 7.2 作業ユーザー

rootでの常用を避ける。

```bash
sudo adduser deploy
sudo usermod -aG sudo deploy
```

SSH公開鍵認証を設定し、パスワードログインは無効化する。

### 7.3 ファイアウォール

公開するポートはSSH、80、443のみとする。

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```

PostgreSQLの5432、アプリの8080は外部公開しない。

### 7.4 Dockerインストール

Docker公式手順に従ってDocker EngineとCompose Pluginを導入する。
導入後に確認する。

```bash
docker --version
docker compose version
```

deployユーザーでDockerを使う場合:

```bash
sudo usermod -aG docker deploy
```

一度ログアウトして再ログインする。

### 7.5 アプリ配置

例:

```bash
sudo mkdir -p /opt/web-writing-tool
sudo chown deploy:deploy /opt/web-writing-tool
cd /opt/web-writing-tool
git clone <repository-url> .
```

Gitを使わずに配置する場合は、CIで生成した成果物またはCompose一式を配置する。

### 7.6 `.env`配置

```bash
cp .env.example .env
chmod 600 .env
```

`.env`には本番値を設定する。
このファイルをGitへコミットしない。

### 7.7 Caddyfile設定

本番ドメインを設定する。

```caddyfile
example.com {
    encode gzip zstd

    reverse_proxy app:8080
}
```

実装時には以下も検討する。

- アクセスログ
- セキュリティヘッダー
- リクエストボディサイズ上限
- `/health/live`の公開可否

### 7.8 DNS設定

ドメインのAレコードまたはAAAAレコードをVPSへ向ける。

確認:

```bash
dig example.com
```

Caddyの証明書発行には、80/443が外部から到達可能である必要がある。

### 7.9 本番起動

```bash
docker compose pull
docker compose up -d
docker compose ps
```

初回ビルド方式の場合:

```bash
docker compose up -d --build
```

### 7.10 初回Migration

本番DBへMigrationを適用する前に、必ずバックアップを取得する。
初回は空DBのためバックアップ対象がない場合でも、手順として確認する。

```bash
docker compose exec app dotnet ef database update
```

実運用では、アプリコンテナにEF CLIを含めない構成もあり得る。
その場合は、Migration Bundleまたはデプロイ用コンテナで適用する。

### 7.11 起動確認

```bash
docker compose ps
docker compose logs --tail=100 app
docker compose logs --tail=100 caddy
docker compose logs --tail=100 postgres
```

ブラウザまたはcurlで確認する。

```bash
curl -I https://example.com
curl https://example.com/health/live
```

確認項目:

- HTTPSでアクセスできる。
- HTTPがHTTPSへリダイレクトされる。
- ログイン画面が表示される。
- 初期Adminでログインできる。
- `/health/ready`が成功する。
- PostgreSQLが外部公開されていない。
- Caddyログに証明書取得エラーがない。

## 8. appsettings方針

### 8.1 Git管理する設定

`appsettings.json`、`appsettings.Development.json`には秘密情報を入れない。

入れてよい例:

- 既定のページサイズ
- ジョブポーリング間隔
- ジョブ同時実行数
- GeminiモデルID
- キャッシュTTLの既定値
- ログレベル

### 8.2 Git管理しない設定

- DBパスワード
- Gemini API Key
- Tavily API Key
- X API Bearer Token
- Discord Webhook URL
- WordPress Application Password
- 初期Adminパスワード

## 9. Data Protection

ASP.NET Core IdentityのCookie、暗号化保存、トークン保護のため、Data Protectionキーは永続化する。

### 9.1 local

ローカルでは既定のユーザープロファイル保存でよい。
Docker Compose localでは`app_keys` volumeへ保存する。

### 9.2 production

productionでは必ず永続volumeへ保存する。

例:

```text
/var/app/keys
```

コンテナ再作成後にキーが失われると、ログインCookieや暗号化済みデータの復号に影響する。

## 10. 外部APIキー準備

### 10.1 Gemini

| 項目 | 値 |
| --- | --- |
| Provider | Google Gemini |
| Model | Google Gemini 3.1 Pro Preview |
| Model ID | `gemini-3.1-pro-preview` |
| Region | Japan |

設定:

```text
AiProviders__Gemini__ApiKey
AiProviders__Gemini__Model=gemini-3.1-pro-preview
AiProviders__Gemini__Region=Japan
```

### 10.2 Tavily

設定:

```text
SearchProviders__Tavily__ApiKey
```

### 10.3 X API

契約:

- Pay-per-use
- Full-Archive Searchは必要時のみ
- 通常`max_results`は100
- 大量調査時は500
- 月間安全上限は10,000から50,000 posts程度から開始

設定:

```text
SearchProviders__X__BearerToken
```

### 10.4 WordPress

WordPress Application Passwordは環境変数ではなく、ユーザーが画面から登録する。

登録時の注意:

- WordPress URLはHTTPSのみ。
- Application PasswordはDB暗号化保存。
- レスポンスと画面再表示に平文を出さない。
- 投稿ステータスの既定値はDraft。

### 10.5 Discord

Discord Webhook URLはユーザーまたは管理者が画面から登録する。

登録時の注意:

- Webhook URLはDB暗号化保存。
- ログとレスポンスへ出さない。
- 通知本文に秘密情報、プロンプト全文、記事本文全文を含めない。

## 11. Adminユーザー準備

### 11.1 初期Admin

初期Adminは初回起動時のSeedで作成する。

設定:

```text
AdminSeed__Email
AdminSeed__Password
```

方針:

- `UserManager` / `RoleManager`を使って作成する。
- `Admin`ロールがなければ作成する。
- 既にAdminユーザーが1人以上存在する場合、Seedは新規作成しない。
- 既存Adminのパスワードを起動時に上書きしない。
- パスワードをログ、監査ログ、レスポンスへ出さない。
- 初回ログイン後は、初期Adminパスワードを変更し、`.env`または秘密情報ファイルから削除または無効化する運用を推奨する。

### 11.2 2人目以降のAdmin

通常運用では、管理画面からAdminを追加する。

方法:

- 既存ユーザーをAdminへ昇格する。
- 管理者が新規ユーザーを作成し、作成時にAdminロールを付与する。

禁止:

- DBへ直接`AspNetUserRoles`をINSERTする。
- 固定Adminパスワードをコードへ埋め込む。
- 起動のたびにAdminパスワードを上書きする。

### 11.3 緊急復旧

Adminが全員ログイン不能になった場合に備え、後続実装でCLIまたは一時Seedコマンドによる復旧導線を用意できる設計にする。
通常運用では使用しない。

## 12. ヘルスチェック

実装予定:

| Endpoint | 用途 | 公開範囲 |
| --- | --- | --- |
| `/health/live` | プロセス生存確認 | 外部監視可 |
| `/health/ready` | DB、BackgroundService確認 | 内部または管理者限定 |
| `/health/deps` | 外部API疎通確認 | 管理者限定 |

確認コマンド:

```bash
curl https://example.com/health/live
```

`ready`と`deps`には秘密情報、接続文字列、外部APIレスポンス全文を出さない。

## 13. よく使うコマンド

### 13.1 build

```powershell
dotnet build
```

または:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
```

### 13.2 test

```powershell
dotnet test
```

または:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
```

### 13.3 format

```powershell
dotnet format
```

または:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/format.ps1
```

### 13.4 Migration追加

```powershell
dotnet ef migrations add <MigrationName> --project src/WebWritingTool.Infrastructure --startup-project src/WebWritingTool.Web
```

### 13.5 Migration適用

```powershell
dotnet ef database update --project src/WebWritingTool.Infrastructure --startup-project src/WebWritingTool.Web
```

### 13.6 Docker Compose

```powershell
docker compose up -d --build
docker compose ps
docker compose logs -f app
docker compose down
```

## 14. トラブルシュート

### 14.1 PostgreSQLへ接続できない

確認:

- `docker compose ps`
- `POSTGRES_DB`
- `POSTGRES_USER`
- `POSTGRES_PASSWORD`
- `ConnectionStrings__DefaultConnection`
- Dockerネットワーク上のホスト名が`postgres`になっているか

### 14.2 Migrationが失敗する

確認:

- DBが起動しているか
- 接続文字列が正しいか
- 既存MigrationとDB状態がずれていないか
- 破壊的変更を含んでいないか

本番ではMigration前に必ずバックアップを取得する。

### 14.3 ログインできない

確認:

- 初期Admin Seedが成功しているか
- 既にAdminユーザーが存在する場合、Seedがパスワードを上書きしない仕様であること
- Data Protectionキーが永続化されているか
- Cookie Secure設定とHTTPSが一致しているか
- CaddyのForwarded HeadersがASP.NET Coreへ伝わっているか

### 14.4 HTTPSに接続できない

確認:

- DNSがVPSへ向いているか
- 80/443が開いているか
- Caddyコンテナが起動しているか
- CaddyログにACMEエラーがないか

### 14.5 Gemini / Tavily / X検索が失敗する

確認:

- APIキーまたはBearer Tokenが設定されているか
- 環境変数名がOptionsクラスと一致しているか
- レート制限または月間安全上限に達していないか
- ログに秘密情報を出さず、エラーコードだけ確認する。

### 14.6 WordPress投稿が失敗する

確認:

- WordPress URLがHTTPSか
- Application Passwordが正しいか
- WordPress REST APIが有効か
- カテゴリIDが存在するか
- `HumanReviewRequired`によりPublishが抑止されていないか

### 14.7 Discord通知が届かない

確認:

- Webhook URLが正しいか
- Webhookが削除されていないか
- Discord側のレート制限に当たっていないか
- 通知本文が長すぎないか

## 15. セキュリティ確認チェックリスト

- [ ] `.env`がGit管理されていない。
- [ ] User Secretsに実APIキーを保存している。
- [ ] appsettingsに秘密情報が入っていない。
- [ ] PostgreSQL 5432を外部公開していない。
- [ ] app 8080を外部公開していない。
- [ ] Caddyのみ80/443を公開している。
- [ ] Data Protectionキーが永続化されている。
- [ ] WordPress Application PasswordがDB暗号化保存される。
- [ ] Discord Webhook URLがDB暗号化保存される。
- [ ] ログにAPIキー、Webhook URL、Application Password、Cookieが出ていない。
- [ ] CaddyでHTTPSアクセスできる。
- [ ] Forwarded HeadersによりCookie Secureとリダイレクトが正しく動く。

## 16. 初回構築完了条件

local:

- [ ] `dotnet build`が成功する。
- [ ] `dotnet test`が成功する。
- [ ] PostgreSQLへMigration適用できる。
- [ ] 初期Adminでログインできる。
- [ ] 管理画面から2人目以降のAdminを追加または昇格できる。
- [ ] 記事一覧画面へアクセスできる。

docker-local:

- [ ] `docker compose up -d --build`で`app`, `postgres`, `caddy`が起動する。
- [ ] Caddy経由でアプリへアクセスできる。
- [ ] PostgreSQLデータが再起動後も保持される。
- [ ] Data Protectionキーが再起動後も保持される。

production:

- [ ] DNSがVPSへ向いている。
- [ ] 80/443のみ公開されている。
- [ ] HTTPS証明書がCaddyで発行される。
- [ ] app、postgres、caddyが自動再起動設定になっている。
- [ ] 本番DBバックアップ手順が確認済み。
- [ ] ログ、ヘルスチェック、Discord運用通知を確認できる。
