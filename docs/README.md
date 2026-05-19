# ドキュメント索引

このディレクトリは Web Writing Tool の設計書、運用資料、実装ガイドを管理する。
実装前は対象タスクに関連する文書を確認し、仕様差分が出た場合は該当文書を同じ変更で更新する。

## まず読む文書

| 文書 | 役割 |
| --- | --- |
| [requirements.md](requirements.md) | 要件、MVP範囲、機能要件の基準 |
| [basic-design.md](basic-design.md) | 全体アーキテクチャ、レイヤー責務、主要な設計判断 |
| [coding-guidelines.md](coding-guidelines.md) | 実装規約、レイヤー別ルール、禁止事項 |
| [../todo.md](../todo.md) | 実装フェーズ、タスクID、完了条件 |

## 設計書

| 領域 | 文書 | 主な内容 |
| --- | --- | --- |
| 画面 | [screen-design.md](screen-design.md) | 画面、導線、表示項目、UI状態 |
| API | [api-design.md](api-design.md) | Minimal API、DTO、レスポンス、バリデーション |
| エラー | [error-codes.md](error-codes.md) | ErrorCode、ProblemDetails、ジョブ失敗理由、画面表示 |
| DB | [db-design.md](db-design.md) | Entity、テーブル、制約、インデックス |
| ジョブ | [job-design.md](job-design.md) | BackgroundService、ジョブ種別、状態遷移、再試行 |
| 外部連携 | [external-integration-design.md](external-integration-design.md) | Gemini、Tavily、X、WordPress、Discord連携 |
| プロンプト | [prompt-design.md](prompt-design.md) | AI生成プロンプト、入力ソース、出力形式、検証 |
| コンテンツ表示 | [content-rendering-design.md](content-rendering-design.md) | Markdown保存、HTML変換、サニタイズ、WordPress投稿HTML |
| 記事品質 | [article-quality-guidelines.md](article-quality-guidelines.md) | SEO、本文品質、出典、X引用、公開前レビュー |
| トピックリスク | [topic-risk-classification.md](topic-risk-classification.md) | normal / strict / compliance_strict分類、TTL、人間確認 |
| 更新保守 | [content-update-maintenance.md](content-update-maintenance.md) | 公開後記事の更新、再検証、統合、削除、再投稿 |

## 非機能・運用

| 領域 | 文書 | 主な内容 |
| --- | --- | --- |
| セキュリティ | [security-design.md](security-design.md) | 認証、認可、秘密情報、XSS/CSRF/SSRF対策 |
| データ保持 | [data-retention-privacy.md](data-retention-privacy.md) | 保持期限、削除、匿名化、プライバシー |
| 設定 | [configuration-reference.md](configuration-reference.md) | 環境変数、Options、DB保存設定、秘密情報の扱い |
| 観測性 | [observability-logging.md](observability-logging.md) | 構造化ログ、メトリクス、ヘルスチェック、アラート |
| テスト | [test-design.md](test-design.md) | 単体、結合、DB、ジョブ、E2E、セキュリティテスト |
| CI/CD | [ci-cd-design.md](ci-cd-design.md) | CI/CD、品質ゲート、成果物、デプロイ方針 |
| 運用 | [operation-design.md](operation-design.md) | VPS運用、監視、バックアップ、障害対応 |
| 環境構築 | [environment-setup.md](environment-setup.md) | ローカル、Docker Compose、VPS環境構築 |

## 変更時の更新先

| 変更内容 | 更新する文書 |
| --- | --- |
| 仕様、MVP範囲、ユーザー操作が変わる | [requirements.md](requirements.md), [screen-design.md](screen-design.md) |
| API、DTO、HTTPステータス、エラー形式が変わる | [api-design.md](api-design.md), [error-codes.md](error-codes.md), [test-design.md](test-design.md) |
| Entity、テーブル、Migration、保持期限が変わる | [db-design.md](db-design.md), [data-retention-privacy.md](data-retention-privacy.md), [test-design.md](test-design.md) |
| ジョブ種別、状態遷移、再試行、キャンセルが変わる | [job-design.md](job-design.md), [observability-logging.md](observability-logging.md), [test-design.md](test-design.md) |
| Gemini、Tavily、X、WordPress、Discord連携が変わる | [external-integration-design.md](external-integration-design.md), [security-design.md](security-design.md), [configuration-reference.md](configuration-reference.md) |
| プロンプト、記事品質、SEO、リスク分類が変わる | [prompt-design.md](prompt-design.md), [article-quality-guidelines.md](article-quality-guidelines.md), [topic-risk-classification.md](topic-risk-classification.md) |
| Markdown、HTML、サニタイズ、WordPress投稿本文が変わる | [content-rendering-design.md](content-rendering-design.md), [security-design.md](security-design.md) |
| 公開後の記事更新、再検証、統合、削除が変わる | [content-update-maintenance.md](content-update-maintenance.md), [operation-design.md](operation-design.md) |
| ログ、監視、アラート、運用手順が変わる | [observability-logging.md](observability-logging.md), [operation-design.md](operation-design.md), [ci-cd-design.md](ci-cd-design.md) |
| 実装規約、プロジェクト構成、開発手順が変わる | [coding-guidelines.md](coding-guidelines.md), [environment-setup.md](environment-setup.md), [../README.md](../README.md) |

## 整理ルール

- 文書を追加したら、この索引とルート [README.md](../README.md) のドキュメント案内を更新する。
- 同じ仕様を複数文書へ重複して詳述しない。詳細は責務を持つ文書に置き、他文書からリンクする。
- セキュリティ、秘密情報、データ保持、外部連携の変更は、実装文書だけでなく非機能文書も更新する。
- `todo.md` のタスク完了時は、関連する設計書の差分有無を確認してからチェックを `[x]` にする。
