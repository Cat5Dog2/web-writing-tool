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
| 共通Caddy構成確認 | 外部ネットワーク経由の到達性と分離 | `external-caddy`ジョブ |
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
**tag pushでは起動しない。** またPRでは`docker-production`ジョブが動かない。tools profileの変更だけは
PR用の`migration-image-gate`ジョブがスキャンとreceipt生成を実行する。
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

#### スキャナへ渡すもの

TrivyへDocker socketは渡さない。各イメージは`docker save`でtarへ書き出し、`--input`で読ませる。
socketを渡すとスキャナコンテナがDockerデーモンを操作でき、これはホストのroot相当である。
9.4のとおり本スクリプトはデプロイ先で実行するため、そのデーモンは同一ホストへ配置した
他アプリのコンテナとvolumeも所有する。第三者イメージへ渡してよい権限ではない。

同じ理由で、スキャナイメージはタグではなくdigestで固定する。`-TrivyImage`は引数なので、
セルフテストではなく**通常実行の開始時**に`^[^@\s]+@sha256:[0-9a-fA-F]{64}$`で検証し、
一致しなければ即座に停止する。部分一致では`host@sha256:x`のような固定されていない値を通してしまう。

各スキャンは`--network none`で実行する。スキャナ自身がネットワーク通信を行えなくするためである。
これはスキャナがネットワークへ到達しないという保証であって、イメージ内容がホストから出ないという
保証ではない。スキャナのstdout/stderrはレポートそのものであり、通常のコマンド出力と同様に
コンソールとCIログへ出る。
Trivyには脆弱性DB以外にもJava index、regoチェック、バージョン通知、VEXリポジトリという更新経路と
telemetryがあり、ネットワークがないと黙って飛ばさず失敗するため、いずれも明示的に抑止する。

`--skip-java-db-update`により、**Javaは意図的にスキャン対象外**とする。現在の対象
（.NETアプリ、Goバイナリ、Alpine）にJARは含まれない。Java indexの取得費用は
916 MiBのダウンロード、ディスク1.4 GB、55秒である（2026-09-04、`mirror.gcr.io`からの参考値。
環境で変動する）。CIはキャッシュボリュームを実行間で保持しないため、これを毎ビルド支払うことになる。

正確な方針は「Javaをスキャンしない」ではなく、**Java成果物を含むイメージは、Java対応を有効化するまで
拒否する**である。JARを含むイメージだけを作り、Java indexのないキャッシュに対して本番と同じフラグで
スキャンした結果、Trivy 0.74.0は部分的な結果を返さずexit 1で停止した（2026-09-05実測）。

```text
ERROR [javadb] The first run cannot skip downloading Java DB
FATAL '--skip-java-db-update' cannot be specified on the first run
```

**ただしこの停止は「Java indexがキャッシュに存在しない間」しか成立しない。** `--skip-java-db-update`は
indexの更新を止めるだけで、使用を止めるものではない。古いindexがボリュームへ残っていれば
first-runエラーは発生せず、そのindexで処理される。キャッシュボリュームは`-TrivyCacheVolume`で
任意に変更できるため、前提が崩れる経路も存在する。

そのため、**DB更新より前にキャッシュボリュームの内容を毎回検査する**。`--network none`、
キャッシュ読取り専用の使い捨てコンテナで`/root/.cache/trivy`直下を列挙し、**`db`以外が
存在すれば停止する**許可リスト方式である。空（初回）は通過する。

列挙結果はいったん変数へ代入し、その終了コードを検査してから走査する。コマンド置換の中で直接
展開すると、`ls`が失敗した場合に展開結果が空になってループが0回で終わり、**読めなかったキャッシュを
「異常なし」と報告してしまう**（fail-open）。

Java index単体ではなく内容全体の許可リストにしている理由は2つある。1つはJava index以外の
未知のキャッシュ種別も拾うため。もう1つは、`-TrivyCacheVolume`に旧`web-writing-tool-trivy-cache`
のようなscan cache（`fanal`）入りのボリュームを指定された場合、**ネットワークを保つDB更新工程へ
渡す前に**止める必要があるためである。検査をDB更新の後に置くと、問題を報告する時点では
すでにネットワーク付きコンテナへそのデータを渡し終えている。

契機はJARが現れること自体であり、Composeへ新しいサービスを追加する場合だけでなく、
既存イメージの中へ混入する場合も含む。その時点でDB更新工程へ`--download-java-db-only`を追加する。

固定するスキャナバージョンを上げる際は、この挙動を再確認する。

```bash
printf 'FROM scratch\nCOPY test.jar /opt/test.jar\n' > Dockerfile   # test.jar は任意のzipでよい
docker build -t trivy-java-fixture:local . && docker save trivy-java-fixture:local -o img.tar
docker run --rm --network none --volume <DB専用volume>:/root/.cache/trivy:ro --volume "$PWD:/scan:ro" \
  <trivy@digest> image --input /scan/img.tar --scanners vuln --cache-backend memory \
  --skip-db-update --skip-java-db-update --offline-scan --severity HIGH,CRITICAL --exit-code 1
```

キャッシュは、DB更新工程では書込み可、スキャン工程では読取り専用でマウントし、スキャン側は
`--cache-backend memory`で自身の成果物をメモリに保持する。Trivyのキャッシュには脆弱性DBだけでなく
package listなどスキャン結果も入るため、書込み可で共有すると、ネットワークを保つDB更新工程が
スキャン対象由来のデータを読めてしまう。Trivy 0.74.0で、読取り専用マウントでも
`--skip-db-update`スキャンが成立することを実測している。`--cache-backend`は上流でexperimental
扱いのため、固定するスキャナバージョンを上げる際は再実測する。

キャッシュボリューム名は`web-writing-tool-trivy-db-cache`へ移行した。旧`web-writing-tool-trivy-cache`は
スキャンが書込み可で共有していた時代のpackage listなどを保持しているため、名前を変えて
「スキャンが一度も書いていないボリューム」から開始する。

#### 旧キャッシュボリュームの撤去

新ボリュームに`db`以外が現れないことを確認してから、旧ボリュームを削除する。前リビジョンの
checkoutが残っているホストでは、削除するとそちらのスキャンがDBを再取得することになる。

```bash
# 新ボリュームの中身が db だけであること
docker run --rm --network none --volume web-writing-tool-trivy-db-cache:/root/.cache/trivy:ro \
  --entrypoint sh <trivy@digest> -c 'ls -1 /root/.cache/trivy'

# 前リビジョンのcheckoutが残っていないことを確認したうえで
docker volume rm web-writing-tool-trivy-cache
```

#### スキャンコンテナのリソース上限

**すべてのTrivyコンテナ**（DB更新、Java index確認、スキャン、証跡取得）に`--memory`、
`--memory-swap`、`--cpus`、`--pids-limit`を設定する。デプロイ先では別アプリと同一ホストを
共有するため、大きなイメージや異常な入力に加え、第三者が配布するDBの取得・展開もホスト全体を
圧迫してはならない。

既定値はメモリ1 GiB、CPU 1、プロセス512である。スコープ内最大のイメージ（112 MBのexport）が
256 MiBで完走すること、およびDB更新（1.2 GBの新規ダウンロードと展開）がこの上限のまま
51秒で完走することを実測した（2026-09-05）。前者に対して約4倍の余裕がある。`--cpus`を1にしているのは、
デプロイ先が小規模VPSであり、2では実質的にマシン全体を占有して同居アプリが毎回影響を受けるため
である。ランナーを専有するCIでは引き上げてよい。**値はローカルで快適な数字ではなく、実行する
ホストの容量から決める。** `--memory-swap`を`--memory`と同値にしてswapを無効化するのは、
ホストがswapし始めると同居アプリが影響を受けるためである。

値は`-ScanMemoryLimit`、`-ScanCpuLimit`、`-ScanPidsLimit`で上書きできるが、**開始時に検証する**。
Dockerは`--memory 0`、`--cpus 0`、`--pids-limit 0`および`-1`をいずれも「無制限」として受け付けるため、
指定したつもりで無制限になる経路を塞ぐ必要がある。ゼロ、負数、空、非数値は起動前に拒否する。

#### ディスクはコンテナ上限の外側

`docker save`が書き出すtarは、リソース上限のかかったコンテナの**外**で、一時ディレクトリを
持つfilesystemへ直接書かれる。デプロイ先では、同居アプリのデータが載るのと同じfilesystemである。
`--memory`などでは防げないため、**イメージごとにexport直前で空き容量を検査**し、
イメージサイズの1.5倍を確保できなければ開始せずに停止する。exportは1つずつ作って消すため、
必要なのは最大イメージ1つ分であり合計ではない。

スキャン工程だけは`--cap-add DAC_OVERRIDE`を戻している。exportディレクトリを所有者限定にする一方、
スキャナはコンテナ内のrootとして動き、ホスト側のディレクトリはスクリプト実行者（別uid）の所有だからである。
DAC_OVERRIDEがないとrootはそれを越えられず、Trivyは自分の入力に対して`permission denied`で落ちる。
Docker Desktopのバインドマウントはposixの所有権を持たないためローカルでは再現せず、Linux CIで初めて現れた。
この権限で増えるのは、このコンテナが見えている範囲（読むべきtar、読取り専用のキャッシュ、読取り専用の
ignoreディレクトリ）に対する権限チェックの回避だけである。他の工程はroot所有のDocker volumeしか読まないため、
戻していない。セルフテストが「スキャン工程だけがDAC_OVERRIDEを持つ」ことを検証する。

**この検査が守るのは一時tar用のfilesystemだけである。** Docker data-root側は対象外で、
主な消費源は次のとおりである（短命コンテナのwritable layerなども消費し得る）。

- `docker compose build --pull`（build cacheとイメージレイヤー）
- 第三者イメージの`docker pull`
- scannerイメージ自体のpull
- Trivy DB用ボリューム（1.2 GB）

また、検査は事前確認であって予約ではない。同時に走る別処理が空き容量を減らす場合までは防げない。
共有VPSでは次のいずれかを運用側で用意する。quotaまたは専用filesystemが最も確実である。

- 事前の空き容量確認と予約余力
- quota付きの専用一時filesystem
- Docker data-rootとアプリDBのfilesystem分離

ネットワークを保つTrivy工程はDB更新の1つだけで、この工程はキャッシュ以外を一切マウントしない。
DBを取得するためにネットワークを保つことは、他の通信を許す理由にならないため、この工程にも
`--skip-version-check`、`--skip-vex-repo-update`、`--disable-telemetry`を渡す。上流ではこれらは
それぞれ独立した呼び出しであり、1つ止めても他は止まらない。

証跡用の`trivy --version`実行もDockerを起動するため、他と同じく引数構築関数を通す。関数を通らない
引数列はセルフテストが検査できず、将来socketや書込みmountが混入しても気付けないためである。
セルフテストは、DB更新工程のマウントを許可リストとして検証し、スキャン工程と証跡工程についても
socketの不在、`--network none`、キャッシュの読取り専用マウント、capability drop、
`no-new-privileges`を検証する。
なお、Trivy以外ではネットワークを使う。`docker compose build --pull`、第三者イメージの`docker pull`、
スキャナイメージ自体のpullがそれにあたる。

実行ごとに、スキャナイメージのdigest、`trivy --version`が返すバージョンと脆弱性DBのタイムスタンプ、
検査した全イメージのimage IDを出力する。指摘は、それを出したスキャナとDBの日時とセットでなければ
意味を持たないためである。

エクスポートは解決済みのimage IDに対して行う。タグはbuild/pullとスキャンの間に差し替えられ得るため、
タグでsaveすると起動するものと別のイメージを検査し得る。これは本スクリプト内の競合を閉じるだけで、
起動したコンテナが検査済みイメージ由来であることの検証を代替しない。tarは1イメージごとに作成し、
スキャン直後に削除する。エクスポートはイメージ内容の可読なコピーであり、Linuxでは一時ディレクトリを
所有者限定にする。

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

そのため`Dockerfile.caddy`でGo 1.27.0を使って`v2.11.4`をビルドし直す。同時にHIGH / CRITICALが出ていた
x/crypto、x/net、x/text、grpc-goもバージョンを上げ、ランタイム層は`apk upgrade`する。
結果としてcaddyイメージのHIGH / CRITICALは0件になり、受容記録は不要になった。

`--replace`は下限保証ではなく強制置換で、指定した版に上下どちらの方向でも固定する。この性質は
両面に効く。修正版`v1.83.1`が出た後も`grpc`が`v1.82.1`に留まりCVE-2026-84304（HIGH）を抱えたのは
このためである。一方でx/cryptoを明示指定しておけば、将来x/netの固定を動かしてもv0.55.0未満へ
落ちない。1つ動かすときは4行すべてを読み直し、常に最新へ上げるのではなく互換性を確認して決める。

CVE-2026-56854（GO-2026-6303、2026-08-28公開）はv0.55.0未満のx/cryptoすべてが対象である。
このイメージが持っていたv0.53.0は`x/net v0.56.0`の固定が連れてきた版だが、`caddy v2.11.4`が
要求するv0.52.0も同じく対象なので、固定が脆弱性を持ち込んだわけではない。v0.55.0への更新で
しか解消しない。

上流がGo 1.26.6以降でビルドしたcaddyイメージを公開したら、`Dockerfile.caddy`を削除して
公式イメージへ戻す。`docker compose config`から対象を取るため、戻してもスキャン範囲は変わらない。

toolsプロファイルの`migrate`は通常の長期稼働サービス用manifestには含めないが、別のスキャンゲートを
必須にする。本番DBへ書き込むコンポーネントなので、短命でソケットを開かないことは未検査でよい理由に
ならない。digest固定、path限定の受容記録、単回使用receiptの詳細は「migrateイメージ」を参照する。

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
| `postgres:16-alpine` | `usr/local/bin/gosu`のGo stdlib 22件 | 起動時にrootを降りるためだけに実行され、PostgreSQLが接続を受ける前に終了する。ソケットを開かず非信頼入力も読まない |
| `postgres:16-alpine` | `libcrypto3` / `libssl3`のCVE-2026-14456 | OpenSSL 3.5のQUICサーバーlistenerでのみ発生する。PostgreSQL 16はQUICを実装せず、TCP上のTLSを従来の`SSL_accept`で終端する。本番Composeは5432を公開せず、`ssl`も無効。公式イメージをそのまま使う現構成では、Alpineの修正版3.5.8-r0は上流の再ビルドで入る |

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

`docker-production`ジョブはスキャン後に`scripts/production-compose.ps1`でmanifestのimage IDを渡して
起動する。`--build`を付けるとスキャン対象と起動対象が別のビルド成果物になるため、ラッパーが拒否する。

本番も同じ制約を持つ。CIでビルドしたイメージはレジストリへpushしていないため、VPSへ配布されない。
本番へ出る成果物はVPS上でビルドしたものであり、**CIのスキャンは本番成果物の保証にはならない**。
そのためVPSでも次の順序を守る。

1. `scripts/scan-image.ps1 -ComposeFile docker-compose.yml -Build -ProvenanceOutputPath artifacts/scanned-images.json`。
   `docker compose build --pull`でビルドし、解決したimage IDをスキャンしてmanifestへ記録する。
2. DBマイグレーションが必要なら`scripts/scan-image.ps1 -ComposeFile docker-compose.yml -ComposeProfile tools -ServiceName migrate -ScanReceiptOutputPath artifacts/scanned-migrate.json`。
3. `scripts/production-compose.ps1 -ComposeFile docker-compose.yml -ComposeCommand '--profile tools run --rm migrate'`。必ず2の直後に行う。
4. `scripts/production-compose.ps1 -ComposeFile docker-compose.yml -ComposeCommand 'up -d --no-build'`。

上記は付属Caddy構成の値である。overrideを使う場合は、1から4の`-ComposeFile`へ同じファイル一覧を渡す。

3を1と2より先に置いてはならない。Migrationを適用してからスキャンが失敗すると、新しいスキーマだけが
DBへ入り、旧appがそれに接続したまま残る。手順を中断しても元に戻らない。
スキャンが先なら、失敗して変わっているのはビルド成果物だけで、DBと稼働中のappは無傷で済む。
`docker-production`ジョブもbuild、scan、ラッパー経由のPostgreSQL起動、Migration、app/Caddy起動の順で動く。
デプロイ手順の全体は[運用設計](operation-design.md)14.2を参照。

### 外部ネットワークの前提

`docker-compose.external-caddy.yml`を使う構成では、共通Caddyとの外部ネットワークが先に存在している
必要がある。不在のとき`docker compose config`は成功し、`docker compose up`だけが失敗する。静的検証で
捕まらないため、デプロイ手順の途中で当たると、ビルド、app停止、バックアップ、Migrationまで進んだ後で
止まる。前節と同じ理由で、状態を変える前に`scripts/preflight-external-caddy.ps1`で確認する。

`external-caddy`ジョブがこの前提を検証する。ネットワーク不在でpreflightが止まること、同じ状態で
`docker compose config`は通ってしまうこと、Caddyネットワーク上のコンテナが`wwt-app:8080`へ到達できる
こと、そこからPostgreSQLへ名前でもアドレスでも到達できないこと、PostgreSQLがホストへ公開されないこと、
appがループバックにだけ公開されることを確認する。

新しい外部Caddy構成そのものをマージ前に検証できるよう、`external-caddy`はPRを含むすべてのCIトリガーで動かす。

### スキャンした成果物を起動する

`--no-build`だけでは、スキャンを通った成果物が起動する保証にならない。塞ぐべき穴が2つある。

**1つ目は呼び出し側の環境。** `docker-compose.yml`は`${APP_IMAGE}`、`${POSTGRES_IMAGE}`、`${CADDY_IMAGE}`で
イメージを選ぶ。Composeでは`--env-file`よりシェルの環境変数が優先される。`scan-image.ps1`は自プロセス内で
`APP_IMAGE`を設定するので正しい成果物を検査するが、その後の`docker compose up`を`APP_IMAGE`をexportした
シェルで実行すると、**別のイメージが正常に起動する**。デプロイの失敗ではなく、ゲートを通っていないものの
デプロイになる。

**2つ目はタグの再解決。** 起動時にタグを引き直す方式も安全ではない。スキャンと起動の間にタグが
別のイメージへ差し替えられる余地が残る。スキャナ自身はこれを避けており、タグを image ID へ解決してから
その ID を`docker save`する。同じ保証を起動側にも持たせる必要がある。

そのため`scan-image.ps1 -ProvenanceOutputPath <path>`が、実際に検査した image ID を機械可読な形で記録する。

```json
{
  "schemaVersion": 1,
  "gateResult": "passed",
  "generatedAt": "2026-09-05T09:26:58Z",
  "scannerImage": "aquasec/trivy@sha256:...",
  "services": {
    "app": { "reference": "web-writing-tool-app:local", "imageId": "sha256:..." },
    "postgres": { "reference": "postgres:16-alpine", "imageId": "sha256:..." }
  }
}
```

このファイルの扱いには2つの規則がある。**書くのは全イメージがゲートを通過した後だけ**で、**パラメーター
バインド後の最初の失敗可能処理として既存ファイルを削除する**。両方が必要である。scanner digest、リソース値、
受容記録、Docker有無の検証で早期終了した場合も、前回成功したmanifestを残さない。古いmanifestが失敗した
実行を生き延びると、ゲートが拒否したイメージがそのままデプロイされる。

キーはイメージではなくサービスである。スキャンは同じイメージを二度検査しないよう重複排除するが、デプロイは
サービスごとに変数を設定するため、2つのサービスが同じイメージを共有する場合も両方の項目が要る。

`scripts/production-compose.ps1`がこのmanifestを読み、image ID を Compose へ渡す。タグを引き直さず、
呼び出し側の環境変数は上書きする。`up`の後は、コマンドが選択した各サービスの稼働中コンテナについて
`.Image`をmanifestと突き合わせる。他の承認済みサービスが稼働しているだけでは成功にしない。
`powershell -File`は引数をすべてパラメータとして解釈するため、Composeコマンドは`-ComposeCommand`へ
1つの文字列で渡す。

```powershell
pwsh -File scripts/production-compose.ps1 -ComposeFile docker-compose.yml -ComposeCommand 'up -d --no-build app'
```

`scan-image.ps1`と`production-compose.ps1`の`-ComposeFile`は同じ値を使う。両方の既定は
`docker-compose.yml`である。overrideを使う構成では両方へ明示する。`-ComposeCommand`内の`-f`、
`--project-name`、`--profile`等は、manifest検証後にプロジェクトやサービス範囲を変更できるため拒否する。
未スキャンのMigrationイメージに関する既知の残存リスクは、完全一致する
`--profile tools run --rm migrate`だけに限定する。`build`、`pull`、`up --build`、`up --pull`も
スキャン後にイメージを変更できるため拒否する。

次のいずれかに当たると起動を拒否する。manifestが無い、JSONとして壊れている、`schemaVersion`が想定外、
`gateResult`が`passed`でない、image IDが64桁のsha256形式でない、スコープ内のサービスがmanifestに無い、
manifestにスコープ外のサービスがある、image変数が定義されていないサービスがある、Composeのスコープを
変えるオプションが`-ComposeCommand`にある、スキャン後にbuild/pullする指示がある、`up`が選択した
サービスのいずれかに稼働中コンテナが無い、またはそのimage IDが一致しない。

`docker-production`ジョブがこの経路を通す。起動ステップでは3つのimage変数へ存在しないタグを意図的に
設定しており、ラッパーがmanifestで上書きしなければイメージが見つからず失敗する。実機では同じ取り違えが
静かに成功してしまうので、CIでは失敗する形にしてある。

`build-test`と`script-compat`は`scripts/test-production-compose.ps1`も実行する。profileや追加Compose
ファイルによるスコープ変更の拒否、対象appが不在でも別のpostgresだけで成功しないこと、リソース値検証のような
早期失敗でも古いmanifestが削除されることを、PowerShell 7とWindows PowerShell 5.1の両方で固定する。

この経路を成立させるため、VPS要件にPowerShell 7を含める。[環境構築](environment-setup.md)3.2を参照。

### migrateイメージ

`migrate`は本番DBへ書き込む唯一のコンポーネントでありながら、`tools` profileにいるため
`docker compose config`に現れず、デプロイ時のスキャンスコープへ入らない。しかも実行内容は
ネットワーク越しの`dotnet restore`と`dotnet tool restore`である。タグのままにすると、レビューして
いない内容が次のデプロイで本番DBを書き換えうる。

そのため次の3点をセットで行う。どれか1つでも欠けると残りが意味を失う。

| 対策 | 場所 | 欠けたときに起きること |
| --- | --- | --- |
| digest固定 | `docker-compose.yml`の`migrate.image` | タグが差し替われば未レビューのイメージが動く |
| 受容記録 | `security/trivy/sdk.trivyignore.yaml` | HIGHが残りゲートが常に赤で、誰も見なくなる |
| デプロイ時スキャンと単回使用receipt | `-ComposeProfile tools -ServiceName migrate -ScanReceiptOutputPath artifacts/scanned-migrate.json` | 固定と受容記録が実際のMigrationに結び付かない |

```powershell
scripts/scan-image.ps1 -ComposeProfile tools -ServiceName migrate -ScanReceiptOutputPath artifacts/scanned-migrate.json
```

`-ComposeProfile`がないとprofile内のサービスはスコープに入らない。`-ServiceName`はデプロイ時の
スキャンが既に見たイメージを再スキャンしないための絞り込みで、`-ProvenanceOutputPath`とは併用できない。
一部のサービスしか載らないmanifestは、残りをタグから起動させてしまうためである。

migrateは長期稼働サービスのmanifestへ入れない。代わりにmigrateだけのreceiptをゲート通過後に原子的に
書き出す。receiptにはimage reference / image ID、scanner digest / version、脆弱性DBの更新時刻、生成時刻を
含める。`production-compose.ps1`は次をすべて満たさない限りMigrationを開始しない。

- receiptが24時間以内で、脆弱性DBの更新時刻が48時間以内である
- scanner imageがdigest固定で、gateResultが`passed`である
- 記録されたreferenceが現在のComposeのmigrate digestと完全一致する
- 記録されたimage IDが、そのdigestからローカルで解決したimage IDと完全一致する
- receiptがmigrate 1サービスだけを含む

検証前に同一filesystem内でreceiptを原子的にclaimし、Migrationの成功・失敗にかかわらず消費する。これにより
並行実行や後日の再利用を拒否する。再試行は新しいスキャンから行う。PRでは`migration-image-gate`、main / schedule /
手動実行では`docker-production`がこの経路を検証する。

現在の受容内容は`System.Security.Cryptography.Xml` 10.0.6 の5件で、いずれもSDKイメージへ同梱された
PowerShell 7.6.4 に含まれる。migrateはbashから`dotnet`を実行するだけでpwshを起動せず、署名付きXMLも
扱わない。appイメージは`mcr.microsoft.com/dotnet/aspnet`ベースでPowerShellを含まないため、稼働中の
アプリからは到達しない。上流が更新版PowerShellでSDKイメージを作り直せば解消する。

digestを更新するときは、新しいdigestを先にスキャンし、HIGH/CRITICALをトリアージしてから
`docker-compose.yml`を変える。受容記録のファイル名はリポジトリ名から決まるため、digestが変わっても
`sdk.trivyignore.yaml`のままである。

将来の選択肢として、CIでビルド・スキャンしたイメージをレジストリへpushし、VPSではdigest指定で
pullして`up --no-build`する方式がある。VPSからビルドツールチェーンを外せ、ロールバックも
digest指定で済む。レジストリの選定と認証情報の配置が前提になるため、導入時に別途決める。

`migrate`については、EF Coreのmigration bundleをイメージビルド時に作り、実行時はSDKもネットワーク
restoreも不要にする案もある。SDKイメージ自体を本番から外せるが、`dotnet ef migrations bundle`が設計時に
`DbContext`を生成するため、ビルド時に接続文字列かデザインタイムファクトリが要る。導入時に別途決める。

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
