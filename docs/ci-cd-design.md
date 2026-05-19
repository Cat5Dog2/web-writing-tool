# CI/CD設計書

## 1. 目的

本書は、AIライティングツールの継続的インテグレーション、成果物作成、リリース前確認、デプロイ、ロールバックの方針を定義する。

対象は、Blazor Web App、ASP.NET Core Minimal API、EF Core/PostgreSQL、BackgroundService、Playwright E2E、Docker Compose、Caddy、VPS運用である。

## 2. 基本方針

- PRでは開発速度を落とさない範囲で品質ゲートを設ける。
- mainマージ後と夜間CIで、PRで省略した重い検証を補完する。
- 外部本番APIはCIで呼ばない。
- PostgreSQL依存テストにはEF Core InMemory Providerを使わない。
- DB MigrationはテストPostgreSQLへの適用またはSQL生成で確認する。
- ローカル開発とPR CIの`dotnet`操作は、共通スクリプト経由で開発用.NET SDKコンテナから実行する。
- 本番デプロイは自動直送せず、手動承認または明示操作を必須にする。
- `.env`、実APIキー、DBパスワード、Webhook URL、Application PasswordをCIログや成果物へ出さない。
- 重大脆弱性が検出された場合はリリースを止める。

## 3. CI/CD基盤

CI/CD基盤はGitHub Actionsとする。

| 項目 | 方針 |
| --- | --- |
| PR CI | GitHub Actionsの`pull_request` workflowで実行する |
| main CI | GitHub Actionsの`push` workflowで実行する |
| 夜間CI | GitHub Actionsの`schedule` workflowで実行する |
| リリース前チェック | GitHub Actionsの`workflow_dispatch`またはrelease tagで実行する |
| production deploy | GitHub Actionsの`workflow_dispatch` + environment protectionで手動承認後に実行する |
| Runner | 初期はGitHub-hosted runnerを使う |
| self-hosted runner | 本番相当性能確認、長時間E2E、VPS近似検証が必要になった段階で検討する |

最小CIはP0で導入し、`scripts/dotnet.ps1`、`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1`を実行する。
本番/配置用Docker確認はP12、テスト品質ゲートの拡張はP13で段階的に追加する。

補助通知Workflowとして`discord-notify`を用意し、push、pull request、Issue closeをDiscord Webhookへ通知する。Webhook URLはGitHub Actions Secretの`DISCORD_WEBHOOK_URL`から参照し、ログや成果物へ出さない。

夜間CIは本番VPSではなくGitHub Actions上で実行する。性能やDocker Composeの本番相当確認がGitHub-hosted runnerでは不十分になった場合のみ、self-hosted runnerまたはリリース前の手動検証へ分離する。

JST 03:00に夜間CIを実行する場合、GitHub ActionsのcronはUTC基準のため以下を使う。

```yaml
on:
  schedule:
    - cron: "0 18 * * *"
```

## 4. 対象ブランチ

| ブランチ / タグ | 用途 | CI/CD |
| --- | --- | --- |
| 作業ブランチ | Issue / タスク単位の実装 | 任意で手動CI |
| PR | mainへ入れる前の品質ゲート | PR CI必須 |
| `main` | 統合済みブランチ | main CI、全E2E、Docker build |
| release tag | リリース候補 | リリース前チェック、成果物固定 |
| production deploy | 本番反映 | 手動承認後に実行 |

mainへのマージ条件は、PR CI成功とレビュー完了とする。

## 5. PR CI

PR CIは必須チェックとする。

実行順:

1. checkout
2. Docker利用可否の確認
3. 開発用.NET SDKコンテナ確認
4. format check
5. build
6. unit tests
7. integration tests
8. DB / Migration tests
9. job tests
10. E2E smoke tests
11. artifact publish

PR CIで実行する範囲:

| 種別 | 対象 | 方針 |
| --- | --- | --- |
| SDK確認 | 開発用.NET SDKコンテナ | `scripts/dotnet.ps1 --info` |
| restore | solution全体 | 共通スクリプト内でNuGet復元 |
| format | solution全体 | `scripts/format.ps1` |
| build | solution全体 | `scripts/build.ps1`。Warningの扱いは実装時に決定 |
| unit tests | `WebWritingTool.UnitTests` | 常時必須 |
| integration tests | `WebWritingTool.IntegrationTests` | WebApplicationFactoryと外部APIモック |
| DB tests | PostgreSQL | Testcontainers for .NETを第一候補 |
| job tests | BackgroundService関連 | ロック、状態遷移、再試行 |
| E2E smoke | Playwright Chromium | 最小セットのみ |
| Docker build | 可能なら軽量確認 | main CIで必須、PRでは時間次第 |

PR CIでは性能テスト、全E2E、本番相当Compose確認を必須にしない。

## 6. main CI

main CIはPRで省略した検証を補完する。

実行対象:

- SDK確認
- restore / build
- format check
- unit tests
- integration tests
- DB / Migration tests
- job tests
- 外部APIモックテスト
- 全E2E
- Docker image build
- publish artifact
- NuGet脆弱性確認

main CIで失敗した場合は、原因を確認し、必要に応じて修正PRを作る。main上で直接修正しない。

## 7. 夜間CI

夜間CIは継続的な劣化検知を目的とする。

実行対象:

| 種別 | 内容 |
| --- | --- |
| 全E2E | `E2E-001`から`E2E-011` |
| 性能テスト | `NFT-PERF-001`から`NFT-PERF-004` |
| データ量増加ケース | 記事、見出し、ジョブ件数を増やした確認 |
| Docker Compose確認 | 本番相当構成の起動確認 |
| 期限切れデータ確認 | X投稿生データTTL、検索キャッシュ削除 |
| 脆弱性確認 | NuGet、Dockerイメージ |

夜間CIの初期段階では、性能テストはリリース停止条件ではなく劣化検知と通知を主目的とする。

## 8. リリース前チェック

リリース前には以下を確認する。

- main CIが成功している。
- 夜間CIまたは直近の全E2Eが成功している。
- Docker image buildが成功している。
- Migration差分を確認済み。
- 破壊的DB変更がない、または段階的Migrationになっている。
- 本番DBバックアップ手順を確認済み。
- `.env.example` と [設定リファレンス](configuration-reference.md) が最新。
- 外部API仕様変更や設定追加が反映済み。
- 秘密情報がログ、成果物、テストデータに含まれていない。
- NuGetとDockerイメージの重大脆弱性がない。
- リリースノートまたは変更概要を用意している。

## 9. ビルド

CIとローカル開発では、ホストの.NET SDKを直接使わず、共通スクリプトを使う。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dotnet.ps1 --info
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/format.ps1
```

テストは以下を使う。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1
```

`global.json`、`Dockerfile.dev`、本番/配置用Dockerfile、CIの.NET SDKバージョンは一致させる。

## 10. テスト実行範囲

| 実行タイミング | 単体 | 結合 | DB | ジョブ | E2E smoke | 全E2E | 性能 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PR | 必須 | 必須 | 必須 | 必須 | 必須 | なし | なし |
| main | 必須 | 必須 | 必須 | 必須 | 必須 | 必須 | 任意 |
| 夜間 | 必須 | 必須 | 必須 | 必須 | 必須 | 必須 | 必須 |
| リリース前 | 必須 | 必須 | 必須 | 必須 | 必須 | 必須 | 手動確認 |

PRのE2E smokeは以下を対象にする。

- `E2E-001` ログイン
- `E2E-002` 記事一覧検索
- `E2E-004` 記事作成
- `E2E-006` 生成結果編集
- `E2E-010` 権限不足

外部APIはモック応答を使う。失敗時のみtrace、screenshot、videoを成果物として保存する。

## 11. PostgreSQL / Migration確認

PostgreSQL依存テストはTestcontainers for .NETを第一候補とする。

Migration確認:

| タイミング | 方針 |
| --- | --- |
| PR | テストPostgreSQLへMigration適用、またはMigration SQL生成 |
| main | テストPostgreSQLへMigration適用 |
| リリース前 | 本番適用用SQLを生成し、破壊的変更を確認 |
| production | DBバックアップ取得後、明示的にMigration適用 |

禁止事項:

- PostgreSQL依存テストにEF Core InMemory Providerを使う。
- 本番DBへCIから接続する。
- 起動時Migration自動適用を本番の標準にする。
- 破壊的Migrationをレビューなしで適用する。

## 12. Docker build

本番/配置用Docker buildはmain CIで必須とする。PR CIでは実行時間を見て軽量確認として扱う。
開発用`Dockerfile.dev`はP0の最小CIでSDK確認、build、test、formatに使う。

確認項目:

- 本番/配置用Dockerfileがビルドできる。
- アプリが非Development設定で起動できる。
- `ASPNETCORE_URLS=http://+:8080`で待ち受ける。
- 不要な開発用秘密情報をイメージに含めない。
- Dockerイメージの脆弱性確認を実行できる。

Docker Compose確認は夜間CIまたはリリース前チェックで行う。

確認項目:

- `app`, `postgres`, `caddy` が起動する。
- PostgreSQLが外部公開されていない。
- app 8080が外部公開されていない。
- Caddy経由でアプリへアクセスできる。
- `/health/live` と `/health/ready` が成功する。

## 13. 外部APIモック方針

CIでは実外部APIを通常呼び出さない。

| 連携 | CI方針 |
| --- | --- |
| Gemini | 固定JSONまたはテストダブル |
| Tavily | 固定JSONまたはテストHandler |
| X API | 固定JSON、上限・TTL・重複排除を検証 |
| WordPress | テストダブル、接続成功/失敗/投稿成功/投稿失敗を検証 |
| Discord | テストダブル、送信成功/失敗/429を検証 |

CI環境変数:

| 変数 | 値 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Test` |
| `ExternalApis__UseMocks` | `true` |
| `Seed__Enabled` | `true` |
| `ConnectionStrings__DefaultConnection` | TestcontainersまたはテストDB |

実APIを使う手動検証は、通常CIとは別の手順として扱う。実APIキーはCIログへ出さない。

## 14. 秘密情報と成果物

CIに入れてよい値:

- テストDB接続文字列
- 外部APIモック切り替え
- ダミーAPIキー
- ダミーWebhook URL

CIに入れない値:

- 本番DB接続文字列
- Gemini API Key
- Tavily API Key
- X API Bearer Token
- WordPress Application Password
- Discord Webhook URL
- `.env`
- Data Protection本番キー

成果物に含めない値:

- `.env`
- User Secrets
- テスト失敗時の秘密情報入りログ
- 外部APIレスポンス全文
- プロンプト全文
- 記事本文全文

## 15. 成果物

PR CIの成果物:

- テスト結果
- カバレッジ結果。導入後
- E2E失敗時のtrace、screenshot、video

main / release CIの成果物:

- published app artifact
- Docker image
- Migration SQL。DB変更がある場合
- テスト結果
- E2E成果物
- 脆弱性確認結果

成果物の保存期間はCIサービスの既定に従う。E2E動画やtraceには秘密情報が映り込まないようにする。

## 16. デプロイ方針

MVPの本番デプロイは、Linux VPS + Docker Compose + Caddyを対象とする。

方針:

- production deployは手動承認または明示操作で開始する。
- 本番DBバックアップを取得してからMigrationを適用する。
- Migrationはデプロイ手順内で明示実行する。
- `docker compose up -d`でサービス更新する。
- デプロイ後にヘルスチェックと最小動作確認を行う。

デプロイ後確認:

- `docker compose ps`
- `/health/live`
- `/health/ready`
- 管理者ログイン
- 記事一覧表示
- 記事作成ジョブ登録
- アプリログ、Caddyログ、PostgreSQLログ

## 17. ロールバック

| 状況 | 方針 |
| --- | --- |
| Migration前のアプリ不具合 | 前バージョンのイメージへ戻す |
| Migration後の軽微な不具合 | 前後方互換があれば前バージョンへ戻す |
| Migration後の重大不具合 | DBバックアップから復元する |
| 外部API障害 | デプロイを戻さず、対象ジョブを再試行待ちまたは停止する |
| Caddy / TLS障害 | Caddy設定、DNS、80/443公開を確認する |

DBスキーマ変更を含むリリースでは、前後方互換のある段階的Migrationを優先する。

## 18. 失敗時の切り分け

| 失敗箇所 | 確認対象 |
| --- | --- |
| SDK確認 | Docker起動、`Dockerfile.dev`、SDKバージョン、作業ディレクトリマウント |
| restore | NuGet接続、SDKバージョン、パッケージ参照 |
| build | コンパイルエラー、TargetFramework、Nullable警告 |
| format | 自動整形差分、生成ファイル除外 |
| unit tests | Domain / Applicationロジック |
| integration tests | DI、認証、API、外部APIモック |
| DB tests | PostgreSQL起動、Migration、接続文字列、制約 |
| job tests | ロック、状態遷移、再試行、テストデータ |
| E2E | Playwrightブラウザ、DB Seed、アプリ起動、trace |
| Docker build | Dockerfile、publish出力、ランタイムイメージ |
| Compose | ネットワーク、volume、ヘルスチェック |
| deploy | `.env`、Migration、イメージタグ、Caddyログ |

失敗ログには秘密情報が出ていないことを確認する。

## 19. 導入順序

1. P0で開発用.NET SDKコンテナを作る。
2. P0で`scripts/dotnet.ps1`、`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1`を作る。
3. P0で最小CIを作り、PRでSDK確認、build、test、formatを実行する。
4. P13で単体テストをCIへ追加する。
5. P13でTestcontainers前提のDB / API結合テストを追加する。
6. P13で外部APIモックテストを追加する。
7. P13でE2E smokeを追加する。
8. P13でmain CIの全E2Eを追加する。
9. P12で本番/配置用Docker buildとCompose確認を追加する。
10. Migration SQL生成と適用確認を追加する。
11. 脆弱性確認を追加する。
12. 手動承認付きproduction deployを追加する。

## 20. 受け入れ基準

- P0の最小CIで`scripts/dotnet.ps1 --info`、`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1`が成功する。
- PR CIでrestore、build、単体、結合、DB、ジョブ、E2E smokeが成功する。
- main CIで全E2Eが成功する。
- CIで外部本番APIを呼ばない。
- PostgreSQL依存テストがPostgreSQLで実行される。
- MigrationがテストDBへ適用できる。
- Docker image buildが成功する。
- `.env`や秘密情報が成果物、ログ、テストデータに含まれない。
- 本番デプロイは手動承認または明示操作でのみ実行される。
- 本番Migration前にDBバックアップを取得する手順がある。
- ロールバック方針が明文化されている。

## 21. 関連ドキュメント

- [テスト設計書](test-design.md)
- [運用設計書](operation-design.md)
- [環境構築手順書](environment-setup.md)
- [設定リファレンス](configuration-reference.md)
- [データ保持・プライバシー設計書](data-retention-privacy.md)
- [セキュリティ設計書](security-design.md)
