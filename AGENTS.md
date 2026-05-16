# AGENTS.md

## 言語

- 返信は日本語で行う。
- 実装判断は簡潔に説明する。

## 最優先参照

実装前に該当する設計書を確認する。

- 要件: `docs/requirements.md`
- 基本設計: `docs/basic-design.md`
- API: `docs/api-design.md`
- DB: `docs/db-design.md`
- 画面: `docs/screen-design.md`
- ジョブ: `docs/job-design.md`
- 外部連携: `docs/external-integration-design.md`
- テスト: `docs/test-design.md`
- 運用: `docs/operation-design.md`
- セキュリティ: `docs/security-design.md`
- 環境構築: `docs/environment-setup.md`
- コーディング規約: `docs/coding-guidelines.md`
- 実装タスク: `todo.md`

## 実装ルール

- `todo.md`のタスクID単位で小さく実装する。
- タスク完了時は該当チェックを`[x]`へ更新する。
- 仕様差分が出たら関連する`docs/*.md`も更新する。
- 既存設計と矛盾する実装をしない。
- 迷った場合は`docs/coding-guidelines.md`を優先する。

## アーキテクチャ

- Blazor Web App + ASP.NET Core Minimal APIを基本とする。
- 認証はASP.NET Core Identityを使う。
- DBはPostgreSQL、ORMはEF Coreを使う。
- 長時間処理はBackgroundServiceジョブとして扱う。
- Web層から`DbContext`を直接操作しない。
- DB Entity、Request DTO、Response DTOを分離する。

## セキュリティ

- UI表示制御だけに頼らず、API/Applicationサービスで認可を検証する。
- 秘密情報、APIキー、Webhook URL、Application Password、Cookie、Authorizationヘッダーをログやレスポンスへ出さない。
- WordPress Application PasswordとDiscord Webhook URLはDB暗号化保存する。
- WordPress投稿の既定ステータスはDraftにする。
- `compliance_strict`または`HumanReviewRequired`の記事は人間確認前にPublishしない。
- production/strictではX投稿の表示・公開前に必ず再取得する。

## テスト

- 実装後は最小の関連テストを実行する。
- PostgreSQL依存の結合テストにEF Core InMemory Providerを使わない。
- 通常の自動テストでGemini、Tavily、X API、WordPress、Discordの実APIを呼ばない。
- 変更内容、確認コマンド、未実行テストを報告する。

## Git

- 明示依頼がない限りcommit、push、PR作成をしない。
- 既存の未追跡・未コミット変更を勝手に戻さない。

# テストコード作成時の厳守事項

## 絶対に守ってください！

### テストコードの品質
- テストは必ず実際の機能を検証すること
- `expect(true).toBe(true)` のような意味のないアサーションは絶対に書かない
- 各テストケースは具体的な入力と期待される出力を検証すること
- モックは必要最小限に留め、実際の動作に近い形でテストすること

### ハードコーディングの禁止
- テストを通すためだけのハードコードは絶対に禁止
- 本番コードに `if (testMode)` のような条件分岐を入れない
- テスト用の特別な値（マジックナンバー）を本番コードに埋め込まない
- 環境変数や設定ファイルを使用して、テスト環境と本番環境を適切に分離すること

### テスト実装の原則
- テストが失敗する状態から始めること（Red-Green-Refactor）
- 境界値、異常系、エラーケースも必ずテストすること
- カバレッジだけでなく、実際の品質を重視すること
- テストケース名は何をテストしているか明確に記述すること

### 実装前の確認
- 機能の仕様を正しく理解してからテストを書くこと
- 不明な点があれば、仮の実装ではなく、ユーザーに確認すること
