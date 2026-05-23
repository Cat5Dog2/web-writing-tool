# Web Writing Tool

AI記事作成、記事管理、本文生成、WordPress投稿、Discord通知を扱うWebアプリケーション。

MVPでは、日本語記事作成を主対象に、キーワード入力から見出し構成、本文生成、記事編集、WordPress下書き投稿、手動公開投稿、通知までを一連のワークフローとして実装する。画像生成、ライター管理、note投稿、課金計算は後続フェーズの対象とする。

## 現在の状態

このリポジトリはP0のプロジェクト土台を実装済みである。

- 実装タスクは [todo.md](todo.md) に定義する。
- 実装前には、対象タスクに関連する `docs/*.md` を確認する。
- タスクは `todo.md` のID単位で小さく進め、完了時にチェックを更新する。
- 開発用.NET SDK Docker環境、Solution、Blazor Web App、Unit/Integrationテスト、最小CIを用意済みである。

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

新規実装時点の想定は .NET 10 / ASP.NET Core 10 である。別バージョンへ固定する場合は、`global.json`、`Dockerfile.dev`、本番/配置用Dockerfile、CI、READMEを同時に更新する。

## 主な機能

- ASP.NET Core Identityによるログイン
- Admin/Userロールによる認可
- 記事の作成、一覧、編集、論理削除
- タイトル候補、見出し構成、本文生成、リライトのジョブ登録
- BackgroundServiceによるジョブ実行、再試行、キャンセル、状態管理
- Geminiによるテキスト生成
- Tavily検索、X API Full-Archive Search、キャッシュ、TTL、重複排除
- X投稿の表示・公開前再取得
- WordPressサイト登録、接続テスト、下書き投稿、手動公開投稿
- Discord Webhook通知設定、送信テスト、ジョブ通知
- WordPress Application PasswordとDiscord Webhook URLの暗号化保存
- Docker Compose + CaddyによるVPS配置

## ドキュメント

ドキュメント全体の索引、読む順番、変更時の更新先は [docs/README.md](docs/README.md) にまとめる。

| 入口 | 内容 |
| --- | --- |
| [docs/README.md](docs/README.md) | 設計書一覧、領域別分類、変更時の更新先 |
| [docs/requirements.md](docs/requirements.md) | 要件、MVP範囲、画面・機能要件 |
| [docs/basic-design.md](docs/basic-design.md) | 全体構成、レイヤー責務、ASP.NET Core設計 |
| [docs/coding-guidelines.md](docs/coding-guidelines.md) | コーディング規約、実装方針 |
| [todo.md](todo.md) | 実装フェーズ、タスクID、完了条件 |

## 実装の進め方

1. [todo.md](todo.md) の上から順にタスクIDを選ぶ。
2. 関連する設計書を確認する。
3. 小さく実装する。
4. 最小の確認コマンドを実行する。
5. 必要に応じて関連ドキュメントを更新する。
6. 完了したタスクのチェックを `[x]` に更新する。

P0のプロジェクト土台は実装済みである。次の実装対象は `todo.md` のP1以降とする。

## ローカル開発の前提

実装開始後は以下のツールを使用する。

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

`T-0000`完了後は、ホストの`dotnet`ではなく開発用.NET SDKコンテナを確認する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet.ps1 --info
```

## 想定コマンド

共通スクリプトは `scripts/` に用意している。

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\format.ps1
```

これらのスクリプトは、ホストの.NET SDKではなく開発用.NET SDKコンテナ経由で実行する。

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
.\scripts\app-up.ps1
```

このスクリプトは、PostgreSQLを起動し、EF Core migrationを適用してからWebアプリを起動する。
また、WindowsとDocker間で `bin/obj` の生成物が混ざってCSS isolationが崩れないよう、起動前にWebプロジェクトをcleanする。
既にmigration適用済みで起動だけしたい場合は、次を使う。

```powershell
.\scripts\app-up.ps1 -SkipMigration
```

cleanも省略したい場合は、`-SkipClean`を追加する。

起動後は以下へアクセスする。

```text
http://localhost:5080/
```

停止:

```powershell
docker compose --env-file .env -f docker-compose.dev.yml down
```

## 秘密情報の扱い

- 実APIキー、DBパスワード、Webhook URL、WordPress Application PasswordをGit管理しない。
- `.env` はGit管理しない。
- `.env.example` にはダミー値のみを置く。
- ローカル開発ではUser Secrets、ローカル環境変数、またはGit管理外の`.env`を使う。
- 開発用.NET SDKコンテナでUser Secretsを使う場合は、保存先を永続volumeまたはホストディレクトリへマウントする。
- WordPress Application PasswordとDiscord Webhook URLはDB暗号化保存する。
- 通常の自動テストでGemini、Tavily、X API、WordPress、Discordの実APIを呼ばない。

## テスト方針

- テストフレームワークはxUnitを基本とする。
- BlazorコンポーネントテストにはbUnitを使う。
- E2EテストにはPlaywright for .NETを使う。
- PostgreSQL依存のテストにEF Core InMemory Providerを使わない。
- 外部APIはモックまたはテストダブルに差し替える。
- 秘密情報、APIキー、Application Passwordをテストログへ出さない。

詳細は [docs/test-design.md](docs/test-design.md) を参照。

## 運用方針

本番運用は Linux VPS + Docker Compose + Caddy を想定する。

- `caddy`: HTTPS終端、リバースプロキシ
- `app`: Blazor UI、API、BackgroundService
- `postgres`: アプリケーションDB

MVPではWebアプリとBackgroundServiceを同じ `app` コンテナで動かす。ジョブ量が増えた場合は、同一イメージからWebとWorkerを分離する。

詳細は [docs/operation-design.md](docs/operation-design.md) を参照。
