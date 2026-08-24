# CI/CD設計書

## 1. 目的

本書は、AIライティングツールの継続的インテグレーション、成果物作成、リリース前確認、デプロイ、ロールバックの方針を定義する。

対象は、Blazor Web App、ASP.NET Core Minimal API、EF Core/PostgreSQL、BackgroundService、Playwright E2E、Docker Compose、Caddy、VPS運用である。

## 2. 基本方針

- PRでは開発速度を落とさない範囲で品質ゲートを設ける。
- mainマージ後と夜間CI、および`workflow_dispatch`で、PRで動かない本番Docker確認と性能テストを補完する。
- 外部本番APIはCIで呼ばない。
- PostgreSQL依存テストにはEF Core InMemory Providerを使わない。
- DB MigrationはテストPostgreSQLへの適用またはSQL生成で確認する。
- ローカル開発とPR CIの通常`dotnet`操作は、共通スクリプト経由で開発用.NET SDKコンテナから実行する。
- Playwright E2E smokeはブラウザとOS依存パッケージが必要なため、GitHub-hosted runner上で.NET SDKをセットアップして実行する。
- E2Eの実行手順は`scripts/test-e2e.ps1`を単一の情報源とし、CIとローカルで同じスクリプトを呼ぶ。CI側に個別の手順を書かない。
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
| リリース前チェック | GitHub Actionsの`workflow_dispatch`で実行する。現行workflowはtag pushでは起動しない |
| production deploy | 未実装。VPS上での手動デプロイを正とする。[運用設計](operation-design.md)14.2の手順に従う |
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
| `main` | 統合済みブランチ | main CI、E2E全件、Docker build、本番Docker確認 |
| release tag | リリース候補の記録 | mainでCIが成功したコミットへ打つ。tag自体はCIを起動しない |
| production deploy | 本番反映 | VPS上で手動実行 |

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
| unit tests | `WebWritingTool.UnitTests` | `scripts/test.ps1`で常時必須 |
| integration tests | `WebWritingTool.IntegrationTests` | `scripts/test.ps1`でWebApplicationFactoryと外部APIモックを検証 |
| DB tests | PostgreSQL | `WebWritingTool.IntegrationTests`内でTestcontainers for .NETを使う |
| job tests | BackgroundService関連 | `WebWritingTool.IntegrationTests`内でロック、状態遷移、再試行を検証 |
| E2E smoke | `WebWritingTool.E2ETests` | `scripts/test-e2e.ps1`。runnerへPlaywright Chromiumをインストールして実行 |
| static analysis | solution全体 | `scripts/dotnet.ps1 slopwatch analyze -d . --fail-on warning` |
| Docker build | 可能なら軽量確認 | main CIで必須、PRでは時間次第 |

PR CIでは性能テストと本番相当Compose確認を必須にしない。E2Eはテストフィルターを掛けず、
E2Eプロジェクト全件を必須にする。現在の実行時間では絞る必要がないためである。

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
- E2Eプロジェクト全件
- Docker image build
- publish artifact
- NuGet脆弱性確認

main CIで失敗した場合は、原因を確認し、必要に応じて修正PRを作る。main上で直接修正しない。

## 7. 夜間CI

夜間CIは継続的な劣化検知を目的とする。

実行対象:

| 種別 | 内容 | 実装 |
| --- | --- | --- |
| E2E | E2Eプロジェクト全件 | `e2e-smoke`ジョブ。テストフィルターを指定しないため、追加したケースは自動で対象になる |
| 性能テスト | `NFT-PERF-001`から`NFT-PERF-004` | `performance`ジョブ。`scripts/test-performance.ps1` |
| データ量増加ケース | 記事、見出し、ジョブ件数を増やした確認 | 性能テストが記事1,000件、見出し100件、ジョブ10,000件を投入して兼ねる |
| Docker Compose確認 | 本番相当構成の起動確認 | `docker-production`ジョブ |
| 期限切れデータ確認 | X投稿生データTTL、検索キャッシュ削除 | 結合テスト |
| 脆弱性確認 | NuGet、Dockerイメージ | `scripts/scan-nuget.ps1`、`scripts/scan-image.ps1` |

夜間CIの初期段階では、性能テストはリリース停止条件ではなく劣化検知と通知を主目的とする。
`performance`ジョブは`continue-on-error: true`で動かし、基準超過はジョブの注釈として残す。

## 8. リリース前チェック

リリース前には以下を確認する。

- main CIが成功している。
- 夜間CIまたは直近のE2E全件が成功している。
- Docker image buildが成功している。
- Migration差分を確認済み。
- 破壊的DB変更がない、または段階的Migrationになっている。
- 本番DBバックアップ手順を確認済み。
- `.env.example`、`.env.production.example` と [設定リファレンス](configuration-reference.md) が最新。
- 外部API仕様変更や設定追加が反映済み。
- 秘密情報がログ、成果物、テストデータに含まれていない。
- NuGetとDockerイメージの重大脆弱性がない。`scripts/scan-nuget.ps1`と`scripts/scan-image.ps1`で確認する。イメージ側は受容記録のないHIGH / CRITICALが0件であることを指す。
- `security/trivy`の受容記録に期限切れがない。
- リリースノートまたは変更概要を用意している。`RELEASE_NOTES.md`へ追記する。
- セキュリティヘッダーが付与されている。結合テスト`SecurityHeadersTests`で検証する。

### 8.1 リリース手順

現行workflowは`pull_request`、`main`へのpush、`schedule`、`workflow_dispatch`で起動する。
**tag pushでは起動しない。** またPRでは`docker-production`ジョブが動かない。
そのためtagは、mainへマージしてCIが成功したコミットへ打つ。

1. 作業ブランチを作る。mainへ直接コミットしない。
2. `RELEASE_NOTES.md`の対象見出しへリリース日とtag名を確定して書く。
3. 変更をコミットする。**リリースノートの確定を先に済ませ、同じコミットへ含める。** 後から更新すると、tagが古いリリースノートを指す。
4. PRを作りmainへマージする。
5. main CIの成功を確認する。`docker-production`ジョブが実行されるのはこのタイミングである。
6. 同じmainのコミットで`workflow_dispatch`を実行し、成功を確認する。`performance`ジョブは
   `schedule`と`workflow_dispatch`でしか動かないため、これがリリース前チェックの実行に当たる。
7. 成功したmainのコミットへtagを作成しpushする。
8. VPSで[運用設計](operation-design.md)14.2の手順によりデプロイする。

将来tag pushでリリース前チェックを起動する場合は、workflowの`on`へ`tags`を追加してから
この手順を見直す。

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

`scripts/test.ps1`はブラウザ不要のテストを開発用.NET SDKコンテナで実行し、`Category=E2E`のテストを常に除外する。E2E smokeは以下で実行する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-e2e.ps1
```

`scripts/test-e2e.ps1`はコンテナではなくホストで、restore、build、Chromiumインストール、`dotnet test`を実行する。ホストに.NET SDKとDockerが必要である。`scripts/test.ps1 -IncludeE2E`はコンテナでの単体・結合テスト後にこのスクリプトへ委譲する。CIのE2Eジョブも同じスクリプトを呼び、手順を二重管理しない。

trxログは`test-results/e2e/trx/e2e.trx`へ出力する。Playwrightのtrace、video、screenshotと同じ`test-results/e2e`配下にまとまるため、CIは失敗時にこのディレクトリごと成果物として保存する。

E2Eをコンテナで実行しない理由は次の2点である。

- `docker-compose.dev.yml`が`ArtifactsPath`を`/tmp`へ向けるため、ビルド出力がバインドマウント外へ出て`E2ETestFixture`がリポジトリルートを解決できない。
- `Dockerfile.dev`にPlaywrightのブラウザーと依存パッケージが含まれていない。

`global.json`、`Dockerfile.dev`、本番/配置用Dockerfile、CIの.NET SDKバージョンは一致させる。

脆弱性スキャンも共通スクリプトを使う。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-nuget.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/scan-image.ps1 -Build
```

| スクリプト | 対象 | 失敗条件 |
| --- | --- | --- |
| `scripts/scan-nuget.ps1` | `dotnet list package --vulnerable --include-transitive`。テストプロジェクトを含む全プロジェクト | High / Criticalが1件以上 |
| `scripts/scan-image.ps1` | 本番Composeがデプロイする全イメージ | 受容記録のないHIGH / CRITICALが1件以上 |

イメージスキャンにはTrivyを使う。Trivyは脆弱性DBをダウンロードするだけで、イメージやその
メタデータを外部サービスへ送信しない。Docker Scoutはイメージ由来情報を外部へ送るため使わない。

### 9.1 スキャン対象イメージ

対象は`docker compose config`から取得する。`docker-compose.yml`と乖離しないようにするためである。
現時点の対象はapp、caddy、postgresの3つである。Caddyはインターネットに面するため必ず含める。

Composeが`build`を持つサービスのイメージは自前ビルドとして扱い、pullせず`docker compose build --pull`で作る。
それ以外はスキャン前に`docker pull`する。ローカルキャッシュのままだと、
上流が公開済みの修正を取り込めていない古いタグを評価してしまう。

#### Caddyを自前ビルドする理由

公開されている`caddy:2.11.4`のバイナリはGo 1.26.3ビルドで、CVE-2026-56862（GO-2026-6090）を含む。
ハンドシェイク後のKeyUpdateメッセージを送り続けることで、TLSサーバーに鍵導出を無期限に実行させるDoSである。
この構成のCaddyはインターネットに面するTLS終端であり、未認証のクライアントから到達できる。
修正はGo 1.25.13 / 1.26.6 / 1.27.0以降にしかなく、再ビルドでしか取り込めない。

そのため`Dockerfile.caddy`でGo 1.27.0を使って`v2.11.4`をビルドし直す。同時にHIGHが出ていた
x/net、x/text、grpc-goもバージョンを上げ、ランタイム層は`apk upgrade`する。
結果としてcaddyイメージのHIGH / CRITICALは0件になり、受容記録は不要になった。

上流がGo 1.26.6以降でビルドしたcaddyイメージを公開したら、`Dockerfile.caddy`を削除して
公式イメージへ戻す。`docker compose config`から対象を取るため、戻してもスキャン範囲は変わらない。

toolsプロファイルの`migrate`（`mcr.microsoft.com/dotnet/sdk:10.0`）はゲート対象外にする。
デプロイ時だけ起動して終了する一時コンテナで、ソケットを開かず常駐しないためである。

自前イメージのビルドにも必ず`--pull`を付ける。ベースイメージがローカルキャッシュのまま古いと、
上流で修正済みのパッチを取り込めず、避けられる脆弱性でゲートが失敗する。

### 9.2 受容済みリスクの扱い

ゲートは`--ignore-unfixed`を使わない。修正版のない脆弱性も、放置ではなく個別の受容記録を必須にする。

受容記録は`security/trivy/<イメージ名>.trivyignore.yaml`へ書く。イメージ単位でファイルを分けるため、
ベースイメージで受容したCVEがアプリイメージの同じCVEを隠すことはない。書式と手順は
[security/trivy/README.md](../security/trivy/README.md)を正とする。

Trivyは`statement`も`expired_at`も任意として扱い、`expired_at`を省略すると無期限に有効になる。
そのため必須化はこちら側で行う。`scripts/scan-image.ps1`はTrivyへ渡す前に次を検証し、
1つでも欠けるとスキャンを失敗させる。検証ロジックはこのスクリプトだけが持ち、他言語へ複製しない。
片方だけ退行しても気付けなくなるためである。`build-test`ジョブがDocker不要の`-ValidateOnly`で
先に実行するため、dockerジョブを待たずに落ちる。

| 項目 | 必須理由 |
| --- | --- |
| `id` | 対象の特定 |
| `paths`または`purls` | 対象限定。同じCVEが同一イメージの別バイナリや別パッケージに出たときに抑制しないため |
| `statement` | CVE単位の到達性評価。コンポーネント全体をまとめた説明にしない |
| `expired_at`（将来日付） | 再トリアージの強制。期限切れはゲートを止める |

現在の受容内容:

| イメージ | 内容 | 理由 |
| --- | --- | --- |
| `postgres:16-alpine` | `usr/local/bin/gosu`のGo stdlib 22件 | 起動時にrootを降りるためだけに実行され、PostgreSQLが接続を受ける前に終了する。ソケットを開かず非信頼入力も読まない。Alpineパッケージ層の指摘は0件 |

`caddy`は受容記録を持たない。到達可能なTLS DoSを受容せず、自前ビルドで解消したためである。9.1参照。

### 9.3 ゲートの動作確認

受容記録の検証は`scripts/scan-image.ps1 -SelfTest`が自動で確認する。
`security/trivy/testdata`のフィクスチャを使い、`id`欠落、`statement`欠落、対象欠落、期限欠落、
非RFC3339、期限切れが実際に失敗し、`paths`指定と`purls`指定の正常系が通ることを確かめる。
CIの`build-test`ジョブが`-ValidateOnly -SelfTest`で毎回実行する。

脆弱性検出そのものが失敗を招くかどうかは、意図的に脆弱な成果物を固定して持つ必要があるため
自動化していない。スキャンスクリプトを変更したときに手で確認する。

| 対象 | 確認方法 |
| --- | --- |
| `scripts/scan-nuget.ps1` | 既知の脆弱バージョンへ一時的に落として実行し、終了コード1と該当パッケージの出力を確認してから戻す |
| `scripts/scan-image.ps1` | `--pull`なしで古いベースイメージからビルドしたイメージを指定し、終了コード1を確認する |

スクリプトは`powershell`（Windows PowerShell 5.1）と`pwsh`（PowerShell 7）の両方で動く必要がある。
CIは両方を実行する。`build-test`が`pwsh`、`script-compat`が`windows-latest`上のWindows PowerShell 5.1で、
`scripts/check-script-encoding.ps1`と`scripts/scan-image.ps1 -ValidateOnly -SelfTest`を回す。

版差で踏んだ実例を2件記録する。

| 事象 | 内容 |
| --- | --- |
| JSON日時の自動変換 | PowerShell 7の`ConvertFrom-Json`はISO-8601文字列を`System.DateTime`へ変換し、文化依存の書式で文字列化する。書式検証をパース結果に対して行うと`pwsh`だけが正常なファイルを拒否した。`-DateKind String`は7.5以降にしかないため、日時の書式はJSONの生テキストから検証する |
| BOMなしの非ASCII | Windows PowerShell 5.1はBOMなしファイルをANSIとして読む。UTF-8の日本語コメントが化けて行末の改行を食い潰し、次行のハッシュテーブル要素が無言で消えた。`scripts/check-script-encoding.ps1`がASCII以外とBOMを拒否する |

### 9.4 スキャン済み成果物の同一性

スキャンを通した成果物と、実際に起動する成果物を一致させる。

`docker-production`ジョブはスキャン後に`docker compose up -d --no-build`で起動する。
`--build`を付けるとスキャン対象と起動対象が別のビルド成果物になるためである。

本番も同じ制約を持つ。CIでビルドしたイメージはレジストリへpushしていないため、VPSへ配布されない。
本番へ出る成果物はVPS上でビルドしたものであり、**CIのスキャンは本番成果物の保証にはならない**。
そのためVPSでも次の順序を守る。

1. `scripts/scan-image.ps1 -Build`。`docker compose build --pull`でビルドし、同じタグをスキャンする。
2. `docker compose up -d --no-build`。

この経路を成立させるため、VPS要件にPowerShell 7を含める。[環境構築](environment-setup.md)3.2を参照。

将来の選択肢として、CIでビルド・スキャンしたイメージをレジストリへpushし、VPSではdigest指定で
pullして`up --no-build`する方式がある。VPSからビルドツールチェーンを外せ、ロールバックも
digest指定で済む。レジストリの選定と認証情報の配置が前提になるため、導入時に別途決める。

## 10. テスト実行範囲

| 実行タイミング | 単体 | 結合 | DB | ジョブ | E2E | 性能 | 本番Docker |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PR | 必須 | 必須 | 必須 | 必須 | 全件 | なし | なし |
| main push | 必須 | 必須 | 必須 | 必須 | 全件 | なし | 必須 |
| 夜間 | 必須 | 必須 | 必須 | 必須 | 全件 | 必須 | 必須 |
| `workflow_dispatch` | 必須 | 必須 | 必須 | 必須 | 全件 | 必須 | 必須 |
| リリース前 | 上記`workflow_dispatch`で全て実行 | | | | | | 手動受け入れを追加 |

E2Eは現行workflowではPRでもフィルターを掛けず全件実行する。性能テストは`schedule`と
`workflow_dispatch`のみで動く。本番Docker確認はPRでは動かない。

PRのE2E smokeで最低限押さえる観点は次のとおり。

- `E2E-001` ログイン
- `E2E-002` 記事一覧検索
- `E2E-004` 記事作成
- `E2E-006` 生成結果編集
- `E2E-010` 権限不足

現行の`e2e-smoke`ジョブはテストフィルターを指定せず、E2Eプロジェクトの全件を実行する。
現在の実行時間では絞る必要がないためである。実行時間が問題になった段階でフィルターを導入する。

外部APIはモック応答またはジョブ登録までの検証を使い、通常CIでは実APIを呼ばない。失敗時のみtrace、screenshot、videoを`test-results/e2e`から成果物として保存する。

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
- `/health/live` と `/health/ready` が成功する。CIのcurlはDockerブリッジ経由でprivate rangeから届くため、Caddyの`/health/ready`制限には掛からない。

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
- `/health/ready`（VPS上からループバック経由）
- 管理者ログイン
- `/health/deps`（管理者。外部APIキーの設定漏れ検知）
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
8. P13でmain CIのE2E全件を追加する。
9. P12で本番/配置用Docker buildとCompose確認を追加する。
10. Migration SQL生成と適用確認を追加する。
11. 脆弱性確認を追加する。
12. 手動承認付きproduction deployを追加する。未実装。現状はVPS上での手動デプロイ。
13. tag pushでのリリース前チェック起動を追加する。未実装。現状はmainのCI成功コミットへtagを打つ運用。

## 20. 受け入れ基準

- P0の最小CIで`scripts/dotnet.ps1 --info`、`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1`が成功する。
- PR CIでrestore、build、単体、結合、DB、ジョブ、E2E smokeが成功する。
- main CIでE2E全件が成功する。
- CIで外部本番APIを呼ばない。
- PostgreSQL依存テストがPostgreSQLで実行される。
- MigrationがテストDBへ適用できる。
- Docker image buildが成功する。
- `.env`や秘密情報が成果物、ログ、テストデータに含まれない。
- 本番デプロイは手動承認または明示操作でのみ実行される。
- 本番Migration前にDBバックアップを取得する手順がある。
- ロールバック方針が明文化されている。

## 21. Docker 実行時の artifacts

`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1` は開発用 .NET SDK コンテナ内で実行する。
コンテナ内の NuGet パスがホスト側の `obj` に残ると、ホスト側の `dotnet test --no-build` がテストプロジェクトを認識できない場合がある。
そのため、SDK コンテナでは `ArtifactsPath` を `/tmp/web-writing-tool-dotnet-artifacts` に設定する。
Docker 経由の build/test は追加で `--artifacts-path` を指定し、ホスト側の `bin` / `obj` を汚さない。

## 22. 関連ドキュメント

- [テスト設計書](test-design.md)
- [運用設計書](operation-design.md)
- [環境構築手順書](environment-setup.md)
- [設定リファレンス](configuration-reference.md)
- [データ保持・プライバシー設計書](data-retention-privacy.md)
- [セキュリティ設計書](security-design.md)
