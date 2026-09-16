# リリースノート

リリース単位で変更概要を記録する。[CI/CD設計](docs/ci-cd-design.md)の「リリース前チェック」で参照する。

新しいリリースは先頭へ追記する。リリース前の変更は`## 未リリース`へ積み、リリース時にその見出しを
バージョン・日付・タグへ書き換える。過去のリリース見出しは当時の記録なので書き換えない。

日付とタグ名は**リリースコミットに含める前に確定させる**。コミット後に更新すると、タグが古い
リリースノートを指すコミットに付く。手順は[CI/CD設計](docs/ci-cd-design.md)8.1を正とする。

## 未リリース

### 記事タイトル候補生成の実装

記事作成画面の「記事タイトル候補を出す」ボタンを実装した。バックエンドのAPI・ジョブハンドラは
既存のまま、常時disabledだったボタンをキーワード入力で有効化し、クリックでタイトル候補生成
ジョブを登録・ポーリングし、完了後に候補選択モーダル（候補一覧、再生成、選択して反映、閉じる）
を表示するようにした。新規記事は先に下書きとして作成し、そのまま「構成を作成」を実行した場合は
新たに記事を作らず同じ下書きを更新することで、重複した下書き記事が残らないようにしている。

### PC表示のUIレビュー対応

記事一覧・記事作成・生成結果編集・設定・ログイン画面のPC表示（1366〜1440px）レビューで見つかった
表示崩れと使い勝手の問題を修正した。記事一覧は操作ボタンが折り返す列幅配分と、記事タイトル・
メモ列の幅配分を見直した。生成結果編集は記事情報を折りたたみ式にし、常時表示のツールバーと、
見出し構成（左）・本文編集（右）を同時に見られる2カラムへ再構成した。記事一覧の見出しが
「記事作成」になっていた表示と、操作できない見た目だけのタブ表示（プロジェクト/詳細/設定）を
一覧・作成画面から削除し、サイドバーの記事一覧/記事作成が同時にactive表示される不具合も修正した。
記事作成画面は詳細設定を初期状態で折りたたみ、実行ボタンが画面外になる問題を解消した。
あわせて「何を作成する？」を「生成モデル」に統一し、無効ボタンに理由を表示し、記事・見出し
ステータスと見出し取得元バッジの表示を日本語に統一し、ログインカードの幅、記事プレビューの
戻るボタンの折り返し、検索0件時の表示を整理した。設定画面のサイト一覧を起点にした再設計と、
製品ブランド名の見直しは今回のスコープ外とした。

### CDリリース候補通知

main CI成功をinfraリポジトリ（`INFRA_REPOSITORY`）へ通知する`notify-release-candidate` workflowを
追加した。同一VPSを使う3リポジトリ体制のうち、web-writing-tool側の担当（自リポジトリのCI成功を
通知するだけ）を実装したもので、本番へのSSH、デプロイ、GHCRへのイメージ配布は行わない。既存のVPS内
ビルド・Trivyスキャン・Migration・イメージのdigest固定は変更していない。

Repository Variable `CD_ENABLED`が文字列`"true"`のときだけ有効になり、未設定時はGitHub Appの秘密鍵
取得や外部への送信より前にスキップする。通知はGitHub Appの短命トークンで認証し、
`repository_dispatch`（`event_type: app-release-candidate-v1`）を送る。設定するVariables/Secrets、
GitHub Appの権限とインストール先、有効化・再実行手順は[運用設計](docs/operation-design.md)22章、
設計は[CI/CD設計](docs/ci-cd-design.md)23章を参照。

### 既定の生成モデル

既定の執筆モデルを `gemini-3.7-flash` から `gemini-3.8-flash` へ切り替えた。`gemini-3.7-flash`、
`gemini-3.6-flash`、`gemini-3.5-flash` は選択肢として残す。既存環境へ適用すると、シードが
`AiModelSettings` の `SortOrder` を再整列し、既定モデルが自動的に `gemini-3.8-flash` へ切り替わる。
既存行の `Enabled` は上書きしない。旧モデルへ戻す手順は[運用設計](docs/operation-design.md)を参照。

### .NET SDKの固定

`mcr.microsoft.com/dotnet/sdk:10.0`と`aspnet:10.0`をタグからdigest固定へ変え、`global.json`を
`10.0.401` + `rollForward: disable`にした。Web SDKが暗黙で足す`Microsoft.AspNetCore.App.Internal.Assets`は
同梱ASP.NET Coreのバージョンに追随し、`packages.lock.json`へ直接参照として残る。タグのままだと上流の
パッチ公開だけでlockファイルと食い違い、`--locked-mode`のrestoreが`NU1004`で落ちていた（2026-09-09の夜間CI）。
`packages.lock.json`は 10.0.12 で再生成した。

E2Eはホストの`dotnet`を使うため、ローカルで実行する場合はホストへ .NET SDK 10.0.401 が必要になった。
それ以外の`dotnet`操作は開発用SDKコンテナ経由のため影響しない。SDKを動かす手順は
[CI/CD設計](docs/ci-cd-design.md)9章「SDKイメージのdigest更新」を正とする。

CIの`build-test`は、restoreしうる最初のコマンドをlocked restoreにし、ジョブ全体へ`RestoreLockedMode`を
効かせた。これまでは`Format check`の暗黙restoreがlockファイルを先に書き換えてしまい、locked restoreが
素通りしていた。

### 脆弱性

| 対象 | 内容 |
| --- | --- |
| Caddyイメージ | `Dockerfile.caddy`の`--replace`へx/cryptoを追加し`v0.55.0`へ更新。CVE-2026-56854（GO-2026-6303）はv0.55.0未満のx/cryptoすべてが対象で、既存の`x/net v0.56.0`固定が連れてきた`v0.53.0`が検出された |
| Caddyイメージ | `grpc`を`v1.82.1`から`v1.83.2`へ更新。CVE-2026-84304（HIGH、GHSA-vp52-pcj8-j9qc）は`v1.83.1`で修正済みだが、`--replace`が`v1.82.1`へ固定していたため取り込めていなかった |
| Caddyイメージ | 併せて`x/net`を`v0.58.0`、`x/text`を`v0.41.0`へ更新。`--replace`は強制置換で上下両方向に固定するため、1つ動かすときは全行を見直す方針を[CI/CD設計](docs/ci-cd-design.md)9.1へ追記した |
| SDKイメージ | `security/trivy/sdk.trivyignore.yaml`を削除。SDK 10.0.400同梱のPowerShell 7.6.4にあった`System.Security.Cryptography.Xml` 10.0.6由来5件は、SDK 10.0.401がPowerShell 7.6.6（同 10.0.10）を同梱したことで解消した。新digestのスキャンはHIGH/CRITICAL 0件 |

## v0.1.0 — MVP初回リリース

- リリース日: 2026-08-24
- タグ: `v0.1.0`
- 対象: MVP主要フロー一式
- DBマイグレーション: `20260519133210_InitialIdentity`、`20260520015427_AddBusinessDatabaseFoundation`
- 初回デプロイのため、既存環境からのスキーマ差分はない

### 機能

| 領域 | 内容 |
| --- | --- |
| 認証・認可 | ASP.NET Core IdentityのCookie認証、Admin/Userロール、所有者認可、初期Admin Seed、本人パスワード変更、本人退会、管理者によるユーザー管理 |
| 記事管理 | 単体・一括作成、検索、ページング、編集、論理削除、見出し編集・並び替え、HTML変換、サニタイズ済みプレビュー |
| バックグラウンド処理 | タイトル・構成・本文・リライト生成、Tavily/X検索、WordPress投稿、Discord通知。DBロック、キャンセル、再試行、失敗記録、期限切れ検索キャッシュ削除 |
| AI・検索 | Geminiテキスト生成、Tavily Search、X API Full-Archive Search、キャッシュTTL、重複排除、トピックリスク分類、X投稿再取得 |
| WordPress | サイト登録・更新・削除、接続テスト、カテゴリ取得、投稿プレビュー、下書き・手動公開投稿、一括作成後の下書き自動投稿、人間確認前の公開抑止 |
| Discord | Webhook通知設定、送信テスト、記事生成完了・WordPress投稿完了・ジョブ失敗の通知 |
| 記事品質 | 記事レビュー、利用量レポート、X引用の投稿前検証 |

### 既定の生成モデル

既定の執筆モデルは `gemini-3.7-flash`。既存環境へ適用すると、シードが `AiModelSettings` の
`SortOrder` を再整列し、既定モデルが自動的に `gemini-3.7-flash` へ切り替わる。
旧モデルへ戻す手順は[運用設計](docs/operation-design.md)を参照。

### デプロイ前ハードニング

| 分類 | 内容 |
| --- | --- |
| セキュリティ | `SecurityHeadersMiddleware`を追加。`X-Content-Type-Options`、`Referrer-Policy`、`X-Frame-Options`、CSPを付与する。CSPは既定`ReportOnly`で、`frame-ancestors 'none'`のみ強制する |
| セキュリティ | `/health/ready`を同梱Caddyでインターネットから遮断。`/health/live`は外形監視のため公開のまま |
| 脆弱性 | `Testcontainers.PostgreSql`を4.12.0から4.14.0へ更新。推移依存の`SSH.NET`が2025.1.0から2026.0.0になり、CVE-2026-48798（High、CVSS 7.1）を解消 |
| 脆弱性 | `scripts/scan-nuget.ps1`と`scripts/scan-image.ps1`を追加し、CIのゲートにした。イメージスキャンはapp、caddy、postgresの全デプロイイメージを対象とし、`--ignore-unfixed`は使わない。pullとビルドに`--pull`を付ける |
| 脆弱性 | Caddyを公開イメージから`Dockerfile.caddy`の自前ビルドへ変更。Go 1.27.0でビルドし直し、インターネット到達可能なTLS DoS（CVE-2026-56862）を含むHIGH 19件を0件にした |
| 脆弱性 | 受容済みリスクを`security/trivy/<イメージ名>.trivyignore.yaml`へ、CVE単位の到達性評価・期限・対象パス付きで記録する運用にした。書式は`scripts/scan-image.ps1`が検証し、`-SelfTest`が異常系の発火を確認する |
| CI | `docker-production`のsmokeを`--no-build`にし、スキャン済みイメージをそのまま起動する |
| 運用 | 本番デプロイ手順を「`scan-image.ps1 -Build`でビルド＋スキャン → `up -d --no-build`」に統一。`up -d --build`をすべて廃止し、起動する成果物とスキャンした成果物を一致させた。VPS要件にPowerShell 7を追加 |
| CI | 非privateな送信元から`/health/ready`が404になることをCIで自動検証する |
| CI | `scripts/check-script-encoding.ps1`を追加し、`scripts/*.ps1`の非ASCIIとBOMを拒否する。`script-compat`ジョブでWindows PowerShell 5.1側も検証する |
| CI | Migrationをデプロイ手順と同じ`docker compose --profile tools run --rm migrate`へ統一。CI専用手順を廃止 |
| CI | 夜間の`performance`ジョブを追加。`NFT-PERF-001`から`NFT-PERF-004`を実行する |
| 設定 | `APP_IMAGE`、`CADDY_IMAGE`、`Security__ContentSecurityPolicyMode`を`.env.production.example`と設定リファレンスへ追加 |
| 開発環境 | 開発用Composeのプロジェクト名を`web-writing-tool-dev`へ分離。本番Composeとコンテナ・volumeを共有しない |

### 脆弱性スキャン結果

| イメージ | HIGH / CRITICAL | 備考 |
| --- | --- | --- |
| アプリイメージ | 0件 | 全深刻度ではLOW 13件、MEDIUM 15件（2026-08-27のCI時点）。大半はUbuntu 24.04パッケージで修正版なし。`openssl` / `libssl3t64`の10件のみ修正版3.0.13-0ubuntu3.15があり、`mcr.microsoft.com/dotnet/aspnet:10.0`の再ビルドで入る |
| Caddyイメージ（自前ビルド） | 0件 | 全深刻度でもMEDIUM 1件のみ。公開イメージのHIGH 19件は再ビルドで解消 |
| `postgres:16-alpine` | 受容記録23件 | 22件は`usr/local/bin/gosu`のGo stdlib。起動時のみ実行され、ソケットを開かず、PostgreSQLが接続を受ける前に終了する。1件はAlpineパッケージ層のCVE-2026-14456（`libcrypto3` / `libssl3`）で、OpenSSL 3.5のQUICサーバーlistener限定のため到達しない |

NuGetは全プロジェクトで脆弱パッケージ0件。
受容内容と再トリアージ手順は[CI/CD設計](docs/ci-cd-design.md)9.2を参照。

### 既知の制約

- CSPは`ReportOnly`。`Enforce`へ移行するには`<ImportMap />`のインラインscriptと`NavMenu.razor`のインライン`onclick`の解消が必要。
- CSP違反レポートの収集エンドポイントは置かない。DevToolsコンソールで確認する。理由は[セキュリティ設計](docs/security-design.md)18.3.1。
- Caddyを自前ビルドしている間は、Caddy本体とGoのリリース追従をこちらが持つ。上流がGo 1.26.6以降でビルドしたcaddyイメージを公開したら`Dockerfile.caddy`を削除して公式イメージへ戻す。
- CIでビルドしたイメージはレジストリへpushしていない。本番成果物はVPS上でビルドするため、ゲートもVPS上で通す必要がある。レジストリ導入でこの二重ビルドは解消できる。
- Caddyイメージに修正版のあるMEDIUMが1件残る（`github.com/google/cel-go`、GO-2026-6094）。ゲート基準はHIGH / CRITICALで、CEL式マッチャーを使っていないため到達しない。
- `postgres:16-alpine`のHIGH / CRITICALは、公式イメージをそのまま使う現在の構成では上流の再ビルド待ちになる。受容期限（gosuのGo stdlib 22件は2026-11-24、`libcrypto3` / `libssl3`のCVE-2026-14456は2026-10-27）を過ぎるとCIが再び失敗し、再トリアージが必要になる。CVE-2026-14456はAlpineが3.5.8-r0で修正済みのため、上流イメージが再ビルドされ次第、受容記録を削除する。gosu由来の22件は`/usr/local/bin/gosu`へ直接配置されたバイナリでapkパッケージではないため、自前イメージで`apk upgrade`しても解消しない。
- 共通Caddy構成では、リポジトリの`Caddyfile`が読まれない。`/health/ready`の遮断はVPS側の共通Caddyへ設定する。
- 外部APIキーの設定漏れは起動時に検知しない。デプロイ後に管理者で`/health/deps`を確認する。
- Data Protectionキー（`app_keys` volume）を失うと、WordPress Application PasswordとDiscord Webhook URLを復号できない。DBバックアップと同じタイミングで退避する。
