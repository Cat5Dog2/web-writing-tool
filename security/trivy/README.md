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
| `postgres:16-alpine` | `usr/local/bin/gosu` | Go stdlib由来22件。エントリーポイントでrootを降りるためだけに実行され、ソケットを開かず非信頼入力も読まない。Alpineパッケージ層は0件 |

`caddy`は受容記録を持たない。公開イメージのバイナリがGo 1.26.3ビルドで、インターネットから到達可能な
TLSのKeyUpdate DoS（CVE-2026-56862）を含んでいたため、修正版Goでビルドし直す
[Dockerfile.caddy](../../Dockerfile.caddy) へ切り替えてHIGH / CRITICALを0件にした。

## 再トリアージ

1. `scripts/scan-image.ps1` のレポート出力で、現在の指摘と修正版の有無を確認する。
2. 上流イメージの更新で解消していれば、該当エントリーを削除する。
3. 解消していなければ、CVE単位で到達性を評価し直し、`statement`と`expired_at`を書き直す。
4. 到達しうると判断したら、受容せずイメージの差し替えか自前ビルドで対応する。`Dockerfile.caddy`が前例。
