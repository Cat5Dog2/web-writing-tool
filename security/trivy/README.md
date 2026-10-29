# Trivy 受容済みリスク

本番Composeがデプロイするイメージのうち、修正できないHIGH / CRITICAL指摘をここへ記録する。
ここに無いHIGH / CRITICALは`scripts/scan-image.ps1`がCIを止める。

方針と再トリアージ手順は [docs/ci-cd-design.md](../../docs/ci-cd-design.md) 9.2を正とする。

## ファイル

`<イメージのリポジトリ名>.trivyignore.yaml` という名前にする。`scripts/scan-image.ps1` がイメージ名から
このファイルを選ぶ。イメージ単位で分けているため、ベースイメージで受容したCVEがアプリイメージの
同じCVEを隠すことはない。

Trivyは拡張子でフォーマットを判定するため`.yaml`のままにするが、中身はJSONで書く。
JSONはYAMLの部分集合なのでTrivyはそのまま読める。PowerShellが追加モジュールなしで
`ConvertFrom-Json`で検証できるようにするためである。

## 記載ルール

`scripts/scan-image.ps1` が Trivy へ渡す前に検証する。1つでも欠けるとスキャンは失敗する。

| フィールド | 必須 | 内容 |
| --- | --- | --- |
| `id` | 必須 | CVE IDまたはGHSA ID |
| `paths` または `purls` | どちらか必須 | 対象を限定する。同じCVEが同一イメージの別バイナリや別パッケージに出た場合に、それまで抑制しないため |
| `statement` | 必須 | 到達性の評価と、修正できない理由 |
| `expired_at` | 必須 | RFC3339。将来日付であること。過ぎるとゲートが再び失敗する |

`statement`はCVE単位の到達性評価を書く。「TLS終端だけだから」のような、対象コンポーネント全体を
まとめた説明にしない。実際にその脆弱性の経路へ到達しうるかを書く。

## 現在の受容内容

| イメージ | 対象 | 概要 |
| --- | --- | --- |
| `postgres:16-alpine` | `usr/local/bin/gosu` | Go stdlib由来26件。エントリーポイントでrootを降りるためだけに実行され、ソケットを開かず非信頼入力も読まない |
| `web-writing-tool-caddy` | `github.com/go-jose/go-jose/v3` v3.0.5 | CVE-2026-116587の1件。修正版が無い。クライアントのJWKを解析するのはACMEサーバー（`acme_server`）だけで、この構成では設定していない |
| `mcr.microsoft.com/dotnet/sdk` | `libbrotli1` 1.1.0-2build2 | CVE-2026-111497の1件。Ubuntuは1.1.0-2ubuntu0.1で修正済みだが、最新の`sdk:10.0`も固定中のdigestのままで、上流の再ビルド待ち。システムのbrotliを読み込むのはcurlとgitのlibcurlだけで、migrateはどちらも実行しない |

2026-10-10の再評価では、最新の`postgres:16-alpine`をpullしても`gosu 1.19`（Go 1.24.6ビルド）が残った。
追加した3件はそれぞれ、HTTPのRangeヘッダー処理（CVE-2026-78667）、HTTP/2のSETTINGS処理
（CVE-2026-78669）、TLSのECH拡張解析（CVE-2026-97031）に限られる。
[gosuの実装](https://github.com/tianon/gosu/blob/1.19/main.go)はユーザーを変更して`exec`するだけで、
HTTPサーバー・クライアントもTLSハンドシェイクも実行しないため、各経路へ到達しない。
上流Dockerfileはgosuのリリースバイナリを取得するため、`apk upgrade`でも置換できない。
対象を`usr/local/bin/gosu`に限定し、既存の再評価期限と同じ`2026-11-24T00:00:00Z`まで受容する。
各CVEの条件と修正版は[Goの公式告知](https://groups.google.com/g/golang-announce/c/U2fTuyDJznI)を参照。

2026-10-28には、Go 1.26.10 / 1.27.3で修正されたCVE-2026-111943（`crypto/x509`、
[Go issue 82345](https://go.dev/issue/82345)）が`gosu`に出た。運用上の懸念がある一部の中間CA証明書を経由する
チェーンを、証明書検証が信頼してしまう問題である。`gosu`はTLSハンドシェイクも証明書検証も行わないため、
受け入れうるチェーンがそもそも存在しない。`gosu 1.20`は2026-10-25に公開されたが、上流の
`postgres:16-alpine`のDockerfileは2026-10-28時点でも`GOSU_VERSION 1.19`のままである。
同じく`usr/local/bin/gosu`に限定し、`2026-11-24T00:00:00Z`まで受容する。受容は合計26件になった。

digest固定したベースの指摘を、Dockerfileの`apt-get upgrade`ではなく受容で扱う理由は
[docs/ci-cd-design.md](../../docs/ci-cd-design.md) 9.2を参照。

2026-10-28の再評価で、公式のSDK 10.0.403 / ASP.NET Core 10.0.14のlinux/amd64イメージ
（2026-10-26作成）が、`libssl3t64` / `openssl`に修正版の3.0.13-0ubuntu3.16を含むことを確認した。
[UbuntuのCVE情報](https://ubuntu.com/security/cves/CVE-2026-84782.json)が示す修正版と一致するため、
`Dockerfile`、`Dockerfile.dev`、`docker-compose.yml`の固定digestを更新し、CVE-2026-84782の受容を解消した。
appとSDKの受容ファイルは削除した。

SDK 10.0.401 / ASP.NET Core 10.0.12には、同梱の.NETライブラリやPowerShellにもHIGH / CRITICALが
残るため、OpenSSLだけを修正した同バージョンのイメージではゲートを通らない。SDK 10.0.403へそろえ、
`global.json`とWebプロジェクトのlockファイルにある暗黙参照`Microsoft.AspNetCore.App.Internal.Assets`も
10.0.14へ更新した。再生成したlockファイルの差分はこの1参照だけである。

SDK 10.0.400の同梱PowerShell 7.6.4にあった`System.Security.Cryptography.Xml` 10.0.6由来の5件は、
SDK 10.0.401がPowerShell 7.6.6（同 10.0.10）を同梱したことで既に解消している。
`scripts/scan-image.ps1`は中身が空の受容ファイルを拒否するので、受容が0件になったらファイルごと消す。

2026-10-29には、`779ee0b`のscheduleのCIで`Production Docker smoke`が、migrateの`libbrotli1` 1.1.0-2build2に
出たCVE-2026-111497（HIGH、Brotliデコーダーの範囲外書き込み、CISA KEV掲載）で失敗した。
[UbuntuのCVE情報](https://ubuntu.com/security/cves/CVE-2026-111497.json)の修正版は1.1.0-2ubuntu0.1
（USN-8956-1、2026-10-26公開）だが、`sdk:10.0`、`sdk:10.0-noble`、`sdk:10.0.403`はいずれも固定中のdigest
（2026-10-26作成）を指したままで、digest更新では解消できない。appの実行基盤の`aspnet`には`libbrotli1`自体が無い。

イメージ内で`libbrotli1`に依存するのは、curlコマンドが使う`libcurl4t64`と、gitのHTTPS転送が使う
`libcurl3t64-gnutls`だけである。.NETはbrotliを`libSystem.IO.Compression.Native`へ静的に組み込んでおり、
このライブラリはlibcとlibmしかリンクしない。`/usr/share/dotnet`と`/usr/share/powershell`にも、
`libbrotlidec`を名前で読み込むファイルは無い。migrateはlocked restore、tool restore、`dotnet-ef database update`を
実行するだけで、curlもgitも起動しない。SDKとこのリポジトリのMSBuildターゲットも、ログインシェルのprofileも
これらを呼ばない。そのため`purls`を1.1.0-2build2に限定して`sdk.trivyignore.yaml`で受容した。
CISA KEV掲載のため、期限は通常の再評価日ではなく、2026-11-10（11月のPatch Tuesday）に予定される
.NETの月例更新の1週間後の`2026-11-17T00:00:00Z`とした。修正版を含むdigestへ動かしたらファイルごと消す。

`caddy`は、公開イメージのバイナリがGo 1.26.3ビルドで、インターネットから到達可能な
TLSのKeyUpdate DoS（CVE-2026-56862）を含んでいたため、修正版Goでビルドし直す
[Dockerfile.caddy](../../Dockerfile.caddy) へ切り替えてHIGH / CRITICALを0件にした。
2026-10-08（UTC）公開のHTTP/TLS指摘も受容せず、Go 1.27.2と`x/net v0.60.0`への更新で対応した。

2026-10-28の再評価で、修正版のある3件はすべて更新で解消した。Go 1.27.3（CVE-2026-111943）、
`go-jose/v4` v4.1.6（CVE-2026-116587、
[GHSA-mgcc-cww5-fp2q](https://github.com/advisories/GHSA-mgcc-cww5-fp2q)）、
`klauspost/compress` v1.18.7（CVE-2026-63209、
[GHSA-259r-337f-4rfw](https://github.com/advisories/GHSA-259r-337f-4rfw)）である。
CVE-2026-116587は`go-jose/v3`にも出たが、v3には修正版が無い。v3は`caddypki`と`caddytls`から
smallstepの`go.step.sm/crypto/jose`経由でリンクされ、smallstepの最新版（`go.step.sm/crypto` v0.91.1）も
Caddyのmainブランチもv3.0.5を要求するため、固定では外せない。

そのため、Caddyで初めての受容記録として`web-writing-tool-caddy.trivyignore.yaml`を置いた。
脆弱な経路は、攻撃者が渡したRSA鍵入りのJWKを解析する処理である。このバイナリでリクエスト由来のJWKを
解析するのは、`acme_server`ハンドラーが公開するACMEサーバーAPI（`smallstep/certificates/acme/api`）だけである。
このリポジトリのCaddyfileにも、wwt-seo-infraの共通Caddyfileにも`acme_server`は無い。`tls internal`のローカルCAは
設定にある名前へ署名するだけで、リクエストからJWKを読まない。公開証明書を取得するACMEクライアントacmezは
go-joseを使わない。共通Caddyは`admin off`で、このリポジトリのCaddyの管理APIもコンテナ内のlocalhostにしか
bindしないため、実行中にACMEサーバーを追加されることもない。`purls`はv3.0.5に限定し、`2026-11-24T00:00:00Z`まで受容する。

ファイル名は、CIと`docker-compose.yml`が使うイメージ名`web-writing-tool-caddy`から決まる。wwt-seo-infraは
同じ`Dockerfile.caddy`から共通Caddyをビルドし、このリポジトリ名のタグでスキャンするため、同じ受容が適用される。

## 再トリアージ

1. `scripts/scan-image.ps1` のレポート出力で、現在の指摘と修正版の有無を確認する。
2. 上流イメージの更新で解消していれば、該当エントリーを削除する。
3. 解消していなければ、CVE単位で到達性を評価し直し、`statement`と`expired_at`を書き直す。
4. 到達しうると判断したら、受容せずイメージの差し替えか自前ビルドで対応する。`Dockerfile.caddy`が前例。
