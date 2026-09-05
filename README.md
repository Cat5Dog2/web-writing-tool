# Web Writing Tool

AI記事作成、記事管理、本文生成、WordPress投稿、Discord通知を扱うWebアプリケーション。

日本語記事作成を主対象に、キーワード入力から見出し構成、本文生成、記事編集、WordPress投稿、通知までのMVP主要フローを実装している。画像生成、ライター管理、note投稿、課金計算は後続フェーズの対象である。

## 現在の状態

MVPの主要フローは実装済みである。[todo.md](todo.md) の実装タスクはすべて完了しており、単体・結合・E2Eテストと本番向けコンテナ構成を含む。

| 状態 | 内容 |
| --- | --- |
| 実装済み | 認証・ロール認可、記事管理、AI生成・検索・投稿・通知ジョブ、外部連携Client、秘密情報保護、PostgreSQL永続化、開発用・本番向けDocker構成、CI |
| 実環境設定が必要 | Gemini、Tavily、X APIの認証情報、WordPressサイトとApplication Password、Discord Webhook URL、本番ドメイン・DNS・TLS到達性、DB/Admin初期値 |
| 設計済み・後続候補 | 画像生成、note投稿、ライター管理、課金計算、複数Worker、Gemini以外のAI Providerなど。詳細は [要件](docs/requirements.md) と [todo.md](todo.md) を参照 |

## 技術スタック

| 区分 | 採用技術 | 用途 |
| --- | --- | --- |
| UI | Blazor Web App | 管理画面、記事編集、フォーム、一覧操作 |
| Backend | ASP.NET Core Minimal API | 認証、認可、API、DI、ヘルスチェック |
| Auth | ASP.NET Core Identity | ログイン、Admin/Userロール |
| ORM | EF Core | PostgreSQLへのデータアクセス、Migration |
| Database | PostgreSQL | ユーザー、記事、ジョブ、外部連携設定の永続化 |
| Background | BackgroundService | AI生成、検索、投稿、通知などの非同期処理 |
| Container | Docker Compose | 開発用.NET SDK、app、postgres、caddyの起動 |
| Reverse Proxy | Caddy | HTTPS終端、リバースプロキシ |
| Hosting | VPS | 単一サーバー運用 |

現在のターゲットは .NET 10 / ASP.NET Core 10 で、SDKは [global.json](global.json) で管理する。

## 主な機能

| 領域 | 実装内容 |
| --- | --- |
| 認証・認可 | ASP.NET Core IdentityのCookie認証、Admin/Userロール、所有者認可、初期Admin Seed、本人パスワード変更、本人退会、管理者によるユーザー管理 |
| 記事管理 | 単体・一括作成、検索、ページング、編集、論理削除、見出し編集・並び替え、HTML変換、サニタイズ済みプレビュー |
| バックグラウンド処理 | `BackgroundService`によるタイトル・構成・本文・リライト、Tavily/X検索、WordPress投稿、Discord通知。DBロック、状態管理、キャンセル、再試行、失敗記録、期限切れ検索キャッシュ削除 |
| AI・検索 | Geminiテキスト生成Client、Tavily Search Client、X API Full-Archive Search Client、キャッシュTTL、重複排除、トピックリスク分類、X投稿再取得 |
| WordPress | サイト登録・更新・削除、接続テスト、カテゴリ取得、投稿プレビュー、下書き・手動公開投稿、一括作成後の下書き自動投稿、人間確認前の公開抑止 |
| Discord | Webhook通知設定、送信テスト、記事生成完了・WordPress投稿完了・ジョブ失敗の通知ジョブ |
| セキュリティ | Data ProtectionによるApplication Password/Webhook URL暗号化、秘密値マスキング、CSRF・XSS・SSRF対策、レート制限、Data Protectionキー永続化 |
| 実行基盤 | EF Core + PostgreSQL Migration、開発用Docker Compose、本番向け`app`/`postgres`/`caddy` Compose、Caddyリバースプロキシ、ヘルスチェック |

外部連携はClientとジョブ処理まで実装済みだが、リポジトリに実APIの認証情報や接続先設定は含まない。本番デプロイ済みであることも前提としない。

## ドキュメント

ドキュメント全体の索引、読む順番、変更時の更新先は [docs/README.md](docs/README.md) にまとめる。

| 入口 | 内容 |
| --- | --- |
| [docs/README.md](docs/README.md) | 設計書一覧、領域別分類、変更時の更新先 |
| [docs/requirements.md](docs/requirements.md) | 要件、MVP範囲、画面・機能要件 |
| [docs/basic-design.md](docs/basic-design.md) | 全体構成、レイヤー責務、ASP.NET Core設計 |
| [docs/external-integration-design.md](docs/external-integration-design.md) | Gemini、Tavily、X、WordPress、Discord連携 |
| [docs/security-design.md](docs/security-design.md) | 認証・認可、秘密情報、CSRF・XSS・SSRF対策 |
| [docs/test-design.md](docs/test-design.md) | 単体、結合、E2E、Dockerテスト |
| [docs/operation-design.md](docs/operation-design.md) | Compose、Caddy、監視、バックアップ、デプロイ方針 |
| [docs/coding-guidelines.md](docs/coding-guidelines.md) | コーディング規約、実装方針 |
| [todo.md](todo.md) | 実装フェーズ、タスクID、完了条件 |

## 実装の進め方

1. [todo.md](todo.md) の上から順にタスクIDを選ぶ。
2. 関連する設計書を確認する。
3. 小さく実装する。
4. 最小の確認コマンドを実行する。
5. 必要に応じて関連ドキュメントを更新する。
6. 完了したタスクのチェックを `[x]` に更新する。

## ローカル開発の前提

| ツール | 用途 |
| --- | --- |
| Docker Desktop | 開発用.NET SDKコンテナ、PostgreSQL、Docker Compose確認 |
| Git | ソース管理 |
| PowerShell 7以上 | 開発用スクリプト実行 |
| .NET SDK 10.x | 任意。原則としてホストには不要 |

確認コマンド:

```powershell
docker --version
docker compose version
git --version
pwsh --version
```

ホストの`dotnet`ではなく開発用.NET SDKコンテナを確認する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet.ps1 --info
```

## ビルド・テスト

共通スクリプトはホストの.NET SDKではなく、開発用.NET SDKコンテナ経由で実行する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/format.ps1
```

`scripts/test.ps1` は `Category=E2E` を常に除外し、単体・結合・PostgreSQL・ジョブ・セキュリティテストを実行する。

Playwright E2Eはコンテナではなくホストで実行する。ホストに.NET SDKとDockerが必要である。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-e2e.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1 -IncludeE2E
```

詳細は [CI/CD設計](docs/ci-cd-design.md) と [環境構築手順書](docs/environment-setup.md) を参照する。

## Dockerでのローカル起動

ローカルでPostgreSQLとWebアプリをまとめて起動する場合は、Git管理外の `.env` を作成してからComposeを起動する。

```powershell
Copy-Item .env.example .env
```

`.env` の `POSTGRES_PASSWORD`、`AdminSeed__Email`、`AdminSeed__Password` をローカル用の値へ変更する。
Gemini、Tavily、X APIを実行する場合は、同じ `.env` の `AiProviders__Gemini__ApiKey`、`SearchProviders__Tavily__ApiKey`、`SearchProviders__X__BearerToken` も実値へ変更する。

既に `postgres_data` volume を作成済みの場合、`POSTGRES_PASSWORD` を変更しても既存DBユーザーのパスワードは自動変更されない。
既存DBを残す場合は、DB作成時と同じ `POSTGRES_PASSWORD` を使う。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/app-up.ps1
```

このスクリプトは、PostgreSQLを起動し、EF Core migrationを適用してからWebアプリを起動する。
また、WindowsとDocker間で `bin/obj` の生成物が混ざってCSS isolationが崩れないよう、起動前にWebプロジェクトをcleanする。
既にmigration適用済みで起動だけしたい場合は、次を使う。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/app-up.ps1 -SkipMigration
```

バックグラウンド起動には `-Detached`、cleanの省略には `-SkipClean` を追加する。

起動後は以下へアクセスする。

```text
http://localhost:5080/
```

停止:

```powershell
docker compose --env-file .env -f docker-compose.dev.yml down
```

開発用Composeのプロジェクト名は `web-writing-tool-dev` である。本番Composeの `web-writing-tool` とコンテナ・volumeを共有しないよう分けている。以前のリビジョンで開発用Composeを起動したことがある場合は、旧プロジェクト名のコンテナを一度削除する。

```powershell
docker compose -p web-writing-tool -f docker-compose.dev.yml --env-file .env down
```

## CI

[`.github/workflows/ci.yaml`](.github/workflows/ci.yaml) はPull Request、`main`へのpush、日次schedule、手動実行を対象とする。

| Job | 実行内容 | 対象 |
| --- | --- | --- |
| `build-test` | Docker確認、開発用.NET SDKコンテナ確認、format check、Slopwatch、NuGet脆弱性スキャン、スクリプト文字コード検査、脆弱性受容記録と本番Compose保護の検証、build、E2Eと性能を除くtest | すべてのトリガー |
| `script-compat` | `windows-latest`上のWindows PowerShell 5.1で文字コード、受容記録、本番Compose保護を検証。CIとVPSは`pwsh`、ローカル手順は`powershell`のため両方で確認する | すべてのトリガー |
| `e2e-smoke` | .NET SDKセットアップ、Playwright Chromium導入、E2Eプロジェクトのテスト実行、失敗時成果物保存 | すべてのトリガー |
| `performance` | `NFT-PERF-001`から`NFT-PERF-004`。劣化検知目的のため `continue-on-error` | schedule、手動実行 |
| `docker-production` | 本番イメージbuild（`--pull`）、イメージ脆弱性スキャン、PostgreSQL起動、Migration、`app`/`caddy`起動、Caddy経由のhealth check | `main`へのpush、schedule、手動実行。PRでは実行しない |
| `external-caddy` | 外部Caddyネットワークのpreflight、`wwt-app` alias経由の到達性、PostgreSQLのネットワーク分離とホスト非公開を検証 | すべてのトリガー |

現行workflowの `e2e-smoke` はテストフィルターを指定していないため、現在のE2Eプロジェクト全件を実行する。Production Docker smokeでは `/health/live` と `/health/ready` を確認する。`/health/deps` は実装済みだが管理者認可が必要で、CI smokeの確認対象外である。

Migrationは本番デプロイ手順と同じ `production-compose.ps1 -ComposeCommand '--profile tools run --rm migrate'` を使う。CI専用の手順を持たない。

### 脆弱性スキャン

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-nuget.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-image.ps1 -Build
```

NuGetはHigh/Criticalが1件でもあれば失敗する。

イメージスキャンは本番Composeがデプロイする全イメージ（app、caddy、postgres）を対象とし、対象一覧は `docker compose config` から取得する。`--ignore-unfixed` は使わず、修正版がない指摘も `security/trivy/<イメージ名>.trivyignore.yaml` へ理由と期限付きで記録しない限りCIを止める。Trivyは脆弱性DBを取得するだけで、イメージやそのメタデータを外部へ送信しない。スキャナにはDocker socketを渡さず、`docker save` したtarを `--input` で読ませる。スキャナイメージはdigestで固定し、各スキャンは `--network none` で実行する。理由は [docs/ci-cd-design.md](docs/ci-cd-design.md) の「スキャナへ渡すもの」を参照。

`migrate` は `tools` profile にいるためこの一覧へ入らないが、本番DBへ書き込む唯一のコンポーネントである。イメージは `docker-compose.yml` 内で digest 固定し、デプロイのたびに専用のスキャンでゲートを通す。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-image.ps1 -ComposeProfile tools -ServiceName migrate
```

`-ProvenanceOutputPath` を付けると、実際に検査した image ID を機械可読な manifest に記録する。書き出すのは全イメージがゲートを通過したときだけで、スキャン開始前に既存ファイルを削除する。前回成功した manifest が失敗した実行を生き延びると、ゲートが拒否したイメージがそのままデプロイされるためである。

本番起動は `scripts/production-compose.ps1` を使う。`--no-build` だけではスキャンを通った成果物が起動する保証にならない。Compose では `--env-file` よりシェルの環境変数が優先されるので、`APP_IMAGE` を export したシェルでデプロイすると別のイメージが**正常に**起動する。ラッパーは manifest の image ID を渡し、`up` の後に起動中コンテナの `.Image` を突き合わせる。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-image.ps1 -ComposeFile docker-compose.yml -Build -ProvenanceOutputPath artifacts/scanned-images.json
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/production-compose.ps1 -ComposeFile docker-compose.yml -ComposeCommand 'up -d --no-build app'
```

現在の受容内容と再トリアージ手順は [docs/ci-cd-design.md](docs/ci-cd-design.md) を参照。

## 秘密情報の扱い

- 実APIキー、DBパスワード、Webhook URL、WordPress Application PasswordをGit管理しない。
- `.env` はGit管理しない。
- `.env.example` と `.env.production.example` にはダミー値のみを置く。
- ローカル開発ではUser Secrets、ローカル環境変数、またはGit管理外の`.env`を使う。
- 開発用.NET SDKコンテナでUser Secretsを使う場合は、保存先を永続volumeまたはホストディレクトリへマウントする。
- WordPress Application PasswordとDiscord Webhook URLはDB暗号化保存する。
- 通常の自動テストでGemini、Tavily、X API、WordPress、Discordの実APIを呼ばない。

## テスト方針

- 単体・結合テストはxUnitを使用する。
- PostgreSQL依存の結合テストはTestcontainers for .NETを使用し、EF Core InMemory Providerは使わない。
- 主要画面フローはPlaywright for .NET + Chromiumで検証する。
- 外部APIはモックまたはテストダブルに差し替える。
- 秘密情報、APIキー、Application Passwordをテストログへ出さない。
- 性能テストは `Category=Performance` で通常実行から除外し、`scripts/test-performance.ps1` だけが実行する。

詳細は [docs/test-design.md](docs/test-design.md) を参照。

## 運用方針

リポジトリには Linux VPS + Docker Compose + Caddy 向けの本番構成を用意している。実際のデプロイ、ドメイン設定、外部API設定、バックアップ・監視設定は環境ごとに実施する必要がある。
本番/配置用Composeでは `.env.production.example` を `.env` へコピーしてから、実値へ変更する。
`.env` は `chmod 600 .env` で所有者だけが読めるようにする。

- `caddy`: HTTPS終端、リバースプロキシ。公開イメージではなく [Dockerfile.caddy](Dockerfile.caddy) から自前ビルドする
- `app`: Blazor UI、API、BackgroundService
- `postgres`: アプリケーションDB

MVPではWebアプリとBackgroundServiceを同じ `app` コンテナで動かす。ジョブ量が増えた場合は、同一イメージからWebとWorkerを分離する。

VPS上の共通Caddyを使う場合は、その動かし方に合わせてoverrideを重ねる。どちらも付属Caddyをprofileで停止し、Migrationはバージョン固定済みのtools profileで明示実行する。どちらを使うかは共通Caddyの動かし方で決まり、置き換え関係ではない。

| override | 共通Caddy | appへの経路 | PostgreSQL |
| --- | --- | --- | --- |
| `docker-compose.shared-caddy.yml` | ホスト上のsystemd等 | `127.0.0.1:8081` | 既存の保守用経路として`127.0.0.1:5433`へ公開（Caddyは使用しない） |
| `docker-compose.external-caddy.yml` | 別Composeプロジェクトのコンテナ | 外部ネットワーク経由で`wwt-app:8080` | ホストへ公開しない |

```bash
pwsh -File scripts/scan-image.ps1 -ComposeFile docker-compose.yml,docker-compose.shared-caddy.yml -Build -ProvenanceOutputPath artifacts/scanned-images.json
pwsh -File scripts/production-compose.ps1 -ComposeFile docker-compose.yml,docker-compose.shared-caddy.yml -ComposeCommand 'up -d postgres'
pwsh -File scripts/production-compose.ps1 -ComposeFile docker-compose.yml,docker-compose.shared-caddy.yml -ComposeCommand '--profile tools run --rm migrate'
pwsh -File scripts/production-compose.ps1 -ComposeFile docker-compose.yml,docker-compose.shared-caddy.yml -ComposeCommand 'up -d --no-build app'
```

Caddyコンテナ構成では、外部ネットワークが先に存在している必要がある。不在でも `docker compose config` は成功し、`docker compose up` で初めて失敗するため、状態を変える前に `scripts/preflight-external-caddy.ps1` で確認する。詳細は [docs/environment-setup.md](docs/environment-setup.md) 7.12 を参照。

スキャンはMigrationより前に置く。逆順にすると、スキャンが失敗したときに新しいスキーマだけがDBへ入り、旧appがそれに接続したまま残る。

`scripts/scan-image.ps1 -Build` が `docker compose build --pull` でイメージを作り、解決したimage IDをスキャンする。起動は同じComposeファイルとmanifestを `production-compose.ps1` へ渡す。`--build` を付けると、スキャンを通した成果物ではなくその場で作り直した別の成果物が動くため、ラッパーが拒否する。

CIでスキャンしたイメージはレジストリへ push していないため、本番へ出る成果物はVPS上でビルドしたものになる。ゲートはVPS上でも通す必要があり、そのためVPS要件にPowerShell 7を含める。

`/health/ready` はDBとBackgroundServiceの稼働状態を返すため、同梱Caddyがインターネットからのアクセスを404で拒否する。共通Caddy構成ではリポジトリの `Caddyfile` が読まれないため、VPS側の共通Caddyに同じ制限を設定する。詳細は [docs/operation-design.md](docs/operation-design.md) を参照。

セキュリティヘッダーはアプリの `SecurityHeadersMiddleware` が付与する。CSPは既定で `ReportOnly` とし、`Security__ContentSecurityPolicyMode=Enforce` で強制へ切り替える。

詳細は [docs/operation-design.md](docs/operation-design.md) を参照。
