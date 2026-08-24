# 検証用フィクスチャ

`scripts/scan-image.ps1 -SelfTest` が使う。受容記録の検証ルールが実際に発火することを確認するためのもので、
実際の受容記録ではない。

拡張子を`.json`にしてあるのは、本物の受容記録の探索パターン`*.trivyignore.yaml`に一致させないためである。

| ファイル | 期待 |
| --- | --- |
| `valid.json` | `paths`指定で通る |
| `purls-only.json` | `purls`指定だけでも通る |
| `escaped-expiry-in-statement.json` | `statement`本文にエスケープされた`expired_at`があっても通る |
| `missing-id.json` | `id`なしで失敗 |
| `missing-statement.json` | `statement`なしで失敗 |
| `missing-scope.json` | `paths`/`purls`なしで失敗 |
| `missing-expiry.json` | `expired_at`なしで失敗 |
| `bare-date-expiry.json` | RFC3339でない日付で失敗 |
| `expired.json` | 期限切れで失敗 |
| `stray-expiry.json` | エントリー外に`expired_at`キーがあり、件数不一致で失敗 |
