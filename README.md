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
| 認証・認可 | ASP.NET Core IdentityのCookie認証、Admin/Userロール、所有者認可、初期Admin Seed、本人退会、管理者によるユーザー管理 |
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

`scripts/test.ps1` は既定で `Category=E2E` を除外し、単体・結合・PostgreSQL・ジョブ・セキュリティテストを実行する。Playwright E2Eの実行手順は [CI/CD設計](docs/ci-cd-design.md) を参照する。

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

## CI

[`.github/workflows/ci.yaml`](.github/workflows/ci.yaml) はPull Request、`main`へのpush、日次schedule、手動実行を対象とする。

| Job | 実行内容 | 対象 |
| --- | --- | --- |
| `build-test` | Docker確認、開発用.NET SDKコンテナ確認、format check、build、E2Eを除くtest | すべてのトリガー |
| `e2e-smoke` | .NET SDKセットアップ、Playwright Chromium導入、E2Eプロジェクトのテスト実行、失敗時成果物保存 | すべてのトリガー |
| `docker-production` | 本番イメージbuild、PostgreSQL起動、Migration、`app`/`caddy`起動、Caddy経由のhealth check | `main`へのpush、schedule、手動実行。PRでは実行しない |

現行workflowの `e2e-smoke` はテストフィルターを指定していないため、現在のE2Eプロジェクト全件を実行する。Production Docker smokeでは `/health/live` と `/health/ready` を確認する。`/health/deps` は実装済みだが管理者認可が必要で、CI smokeの確認対象外である。

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

詳細は [docs/test-design.md](docs/test-design.md) を参照。

## 運用方針

リポジトリには Linux VPS + Docker Compose + Caddy 向けの本番構成を用意している。実際のデプロイ、ドメイン設定、外部API設定、バックアップ・監視設定は環境ごとに実施する必要がある。
本番/配置用Composeでは `.env.production.example` を `.env` へコピーしてから、実値へ変更する。

- `caddy`: HTTPS終端、リバースプロキシ
- `app`: Blazor UI、API、BackgroundService
- `postgres`: アプリケーションDB

MVPではWebアプリとBackgroundServiceを同じ `app` コンテナで動かす。ジョブ量が増えた場合は、同一イメージからWebとWorkerを分離する。

詳細は [docs/operation-design.md](docs/operation-design.md) を参照。
