# Web Writing Tool

AI記事作成、記事管理、本文生成、WordPress下書き投稿、Discord通知を扱うWebアプリケーション。

MVPでは、日本語記事作成を主対象に、キーワード入力から見出し構成、本文生成、記事編集、WordPress下書き投稿、通知までを一連のワークフローとして実装する。画像生成、ライター管理、note投稿、課金計算は後続フェーズの対象とする。

## 現在の状態

このリポジトリは設計書と実装タスクが先行している段階である。

- 実装タスクは [todo.md](todo.md) に定義する。
- 実装前には、対象タスクに関連する `docs/*.md` を確認する。
- タスクは `todo.md` のID単位で小さく進め、完了時にチェックを更新する。
- 現時点では `src/`、`tests/`、`scripts/`、Docker関連ファイルは未作成である。

## 技術スタック

| 区分 | 採用技術 | 用途 |
| --- | --- | --- |
| UI | Blazor Web App | 管理画面、記事編集、フォーム、一覧操作 |
| Backend | ASP.NET Core Minimal API | 認証、認可、API、DI、ヘルスチェック |
| Auth | ASP.NET Core Identity | ログイン、Admin/Userロール |
| ORM | EF Core | PostgreSQLへのデータアクセス、Migration |
| Database | PostgreSQL | ユーザー、記事、ジョブ、外部連携設定の永続化 |
| Background | BackgroundService | AI生成、検索、投稿、通知などの非同期処理 |
| Container | Docker Compose | app、postgres、caddyの起動 |
| Reverse Proxy | Caddy | HTTPS終端、リバースプロキシ |
| Hosting | VPS | 単一サーバー運用 |

新規実装時点の想定は .NET 10 / ASP.NET Core 10 である。別バージョンへ固定する場合は、`global.json`、Dockerfile、CI、READMEを同時に更新する。

## 主な機能

- ASP.NET Core Identityによるログイン
- Admin/Userロールによる認可
- 記事の作成、一覧、編集、論理削除
- タイトル候補、見出し構成、本文生成、リライトのジョブ登録
- BackgroundServiceによるジョブ実行、再試行、キャンセル、状態管理
- Geminiによるテキスト生成
- Tavily検索、X API Full-Archive Search、キャッシュ、TTL、重複排除
- X投稿の表示・公開前再取得
- WordPressサイト登録、接続テスト、下書き投稿
- Discord Webhook通知設定、送信テスト、ジョブ通知
- WordPress Application PasswordとDiscord Webhook URLの暗号化保存
- Docker Compose + CaddyによるVPS配置

## ドキュメント

| ドキュメント | 内容 |
| --- | --- |
| [docs/requirements.md](docs/requirements.md) | 要件、MVP範囲、画面・機能要件 |
| [docs/basic-design.md](docs/basic-design.md) | 全体構成、レイヤー責務、ASP.NET Core設計 |
| [docs/api-design.md](docs/api-design.md) | APIエンドポイント、DTO、レスポンス方針 |
| [docs/error-codes.md](docs/error-codes.md) | ErrorCode、ProblemDetails、ジョブ失敗理由、画面表示方針 |
| [docs/observability-logging.md](docs/observability-logging.md) | 構造化ログ、メトリクス、ヘルスチェック、アラート方針 |
| [docs/db-design.md](docs/db-design.md) | Entity、テーブル、制約、インデックス |
| [docs/screen-design.md](docs/screen-design.md) | 画面、導線、表示項目 |
| [docs/job-design.md](docs/job-design.md) | BackgroundService、ジョブ種別、状態遷移 |
| [docs/external-integration-design.md](docs/external-integration-design.md) | Gemini、Tavily、X、WordPress、Discord連携 |
| [docs/prompt-design.md](docs/prompt-design.md) | AI生成プロンプト、入力ソース、出力形式、検証方針 |
| [docs/content-rendering-design.md](docs/content-rendering-design.md) | Markdown保存、HTML変換、サニタイズ、WordPress投稿HTML方針 |
| [docs/article-quality-guidelines.md](docs/article-quality-guidelines.md) | SEO/SEOライティング、記事タイトル、本文、出典、X引用、公開前レビューの品質基準 |
| [docs/topic-risk-classification.md](docs/topic-risk-classification.md) | normal/strict/compliance_strict判定、TTL、X再取得、人間確認の分類方針 |
| [docs/security-design.md](docs/security-design.md) | 認証、認可、秘密情報、XSS/CSRF/SSRF対策 |
| [docs/data-retention-privacy.md](docs/data-retention-privacy.md) | データ保持、削除、匿名化、プライバシー方針 |
| [docs/test-design.md](docs/test-design.md) | 単体、結合、DB、ジョブ、E2E、セキュリティテスト |
| [docs/ci-cd-design.md](docs/ci-cd-design.md) | CI/CD、品質ゲート、成果物、デプロイ方針 |
| [docs/operation-design.md](docs/operation-design.md) | VPS運用、監視、バックアップ、障害対応 |
| [docs/environment-setup.md](docs/environment-setup.md) | ローカル、Docker Compose、VPS環境構築 |
| [docs/configuration-reference.md](docs/configuration-reference.md) | 設定値、環境変数、Options、秘密情報の扱い |
| [docs/coding-guidelines.md](docs/coding-guidelines.md) | コーディング規約、実装方針 |
| [todo.md](todo.md) | 実装フェーズ、タスクID、完了条件 |

## 実装の進め方

1. [todo.md](todo.md) の上から順にタスクIDを選ぶ。
2. 関連する設計書を確認する。
3. 小さく実装する。
4. 最小の確認コマンドを実行する。
5. 必要に応じて関連ドキュメントを更新する。
6. 完了したタスクのチェックを `[x]` に更新する。

最初の実装対象は `T-0001` のSolution構成作成である。以降、Blazor Web App、レイヤー分割、テストプロジェクト、共通スクリプトの順に進める。

## ローカル開発の前提

実装開始後は以下のツールを使用する。

| ツール | 用途 |
| --- | --- |
| .NET SDK 10.x | ASP.NET Core、EF Core、テスト実行 |
| Docker Desktop | PostgreSQL、Docker Compose確認 |
| Git | ソース管理 |
| PowerShell 7以上 | 開発用スクリプト実行 |

確認コマンド:

```powershell
dotnet --info
docker --version
docker compose version
git --version
pwsh --version
```

## 想定コマンド

実装開始後は、共通スクリプトを `scripts/` に用意する。

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\format.ps1
```

スクリプトが未整備の段階では、対象タスクに応じて `dotnet build` や `dotnet test` を直接実行する。

## 秘密情報の扱い

- 実APIキー、DBパスワード、Webhook URL、WordPress Application PasswordをGit管理しない。
- `.env` はGit管理しない。
- `.env.example` にはダミー値のみを置く。
- ローカル開発ではUser Secretsまたはローカル環境変数を使う。
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
