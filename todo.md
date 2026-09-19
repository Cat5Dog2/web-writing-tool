# 実装タスク分解表

## 0. 進め方

この`todo.md`は、Codexで実装を進める前提の作業チェックリストである。
上から順に進めることを基本とし、各タスクは「実装」「最小確認」「関連ドキュメント更新」を1セットで完了扱いにする。

## 1. 実装ルール

- 既存設計書を正とする。
- 実装前に関連する`docs/*.md`を確認する。
- 1タスクの変更範囲を小さく保つ。
- 破壊的なDB変更はMigration作成前に設計書との差分を確認する。
- 秘密情報、APIキー、Webhook URL、Application Passwordをログ、レスポンス、テストデータへ出さない。
- 外部APIの実呼び出しは通常テストでは行わず、Clientはモックまたはテストダブルに差し替える。
- 実装後は最小の確認コマンドを実行し、失敗時は原因を直してから次へ進む。

## 2. MVP完了条件

- .NET / ASP.NET Core Blazor Web Appが起動する。
- ASP.NET Core Identityでログインできる。
- Admin / Userロールで認可が分離される。
- Adminが他ユーザーを削除でき、対象ユーザーに紐づく業務データが物理削除される。
- ユーザー本人が退会でき、対象ユーザーに紐づく業務データが物理削除される。
- 記事の作成、一覧、編集、論理削除ができる。
- 見出し構成、本文生成、リライトをジョブとして登録できる。
- BackgroundServiceがジョブを取得し、成功、失敗、再試行、キャンセルを扱える。
- Google Gemini 3.8 Flashによるテキスト生成Clientが実装される。
- Tavily検索、X API Full-Archive SearchのClient、キャッシュ、重複排除、TTLが実装される。
- X投稿を表示または公開利用する前に再取得できる。
- WordPressサイト登録、接続テスト、下書き投稿ができる。
- Discord Webhook通知設定、送信テスト、ジョブ通知ができる。
- WordPress Application PasswordとDiscord Webhook URLが暗号化保存される。
- 本番/配置用Docker ComposeとCaddyでVPS配置できる構成がある。
- 主要な単体テスト、結合テスト、セキュリティテストが通る。

## 3. フェーズ一覧

| フェーズ | 目的 | 主な成果物 |
| --- | --- | --- |
| P0 | プロジェクト土台 | 開発用.NET SDK Docker環境、Solution、Blazor Web App、テストプロジェクト、最小CI |
| P1 | 認証・認可 | Identity、Admin/User、ログイン |
| P2 | DB基盤 | Entity、DbContext、Migration、Seed |
| P3 | 記事管理 | 記事CRUD、一覧、論理削除 |
| P4 | ジョブ基盤 | Queue、BackgroundService、状態管理 |
| P5 | AI生成 | Gemini Client、タイトル、構成、本文 |
| P6 | 検索連携 | Tavily、X、TTL、strict判定 |
| P7 | 生成結果編集 | 見出し編集、本文編集、HTML変換 |
| P8 | WordPress連携 | サイト管理、接続テスト、下書き投稿 |
| P9 | Discord通知 | 通知設定、送信テスト、ジョブ通知 |
| P10 | 管理者機能 | ユーザー管理、ユーザー物理削除 |
| P11 | セキュリティ | CSRF、XSS、SSRF、秘密情報保護 |
| P12 | 運用 | 本番/配置用Docker Compose、Caddy、ヘルスチェック、Docker CI確認 |
| P13 | テスト仕上げ | 単体、結合、E2E、受け入れ確認、CI品質ゲート |

## 4. P0 プロジェクト土台

- [x] `T-0000` 開発用.NET SDK Docker環境を整備する。
  - 対象: .NET SDK、NuGet cache、作業ディレクトリマウント
  - 参照: `docs/environment-setup.md`, `docs/configuration-reference.md`, `docs/coding-guidelines.md`
  - 成果物: `Dockerfile.dev`, `docker-compose.dev.yml`, `.dockerignore`
  - 完了条件: ホストに.NET SDKを入れずに、Docker経由で`dotnet --info`を実行できる。

- [x] `T-0001` Solution構成を作成する。
  - 参照: `docs/basic-design.md`
  - 成果物: `src/`, `tests/`
  - 完了条件: 開発用.NET SDKコンテナ経由で`dotnet build`が通る。

- [x] `T-0002` Blazor Web Appプロジェクトを作成する。
  - App model: Blazor Web App
  - Render mode: 画面設計に合わせてInteractive Server中心
  - 完了条件: 開発用.NET SDKコンテナ経由でローカル起動してトップページが表示される。

- [x] `T-0003` Application / Infrastructure / Webの責務を分ける。
  - 完了条件: DI登録方針とフォルダ構成が`docs/basic-design.md`と一致する。

- [x] `T-0004` テストプロジェクトを作成する。
  - 対象: Unit、Integration
  - 完了条件: 開発用.NET SDKコンテナ経由で空のテストが実行できる。

- [x] `T-0005` 共通ビルド・テストスクリプトを整備する。
  - 参照: `docs/environment-setup.md`, `docs/coding-guidelines.md`
  - 条件: ホストの.NET SDKではなく、開発用.NET SDKコンテナ経由で実行する。
  - 候補: `scripts/dotnet.ps1`, `scripts/build.ps1`, `scripts/test.ps1`, `scripts/format.ps1`
  - 完了条件: Codexから同じコマンドでDocker経由の`dotnet build`、`dotnet test`、format確認を実行できる。

- [x] `T-0006` 最小CIを整備する。
  - 参照: `docs/ci-cd-design.md`, `docs/environment-setup.md`
  - 対象: GitHub Actions
  - 条件: 共通ビルド・テストスクリプトを使い、ホストの.NET SDKではなくDocker経由で実行する。
  - 完了条件: PRで`scripts/dotnet.ps1 --info`、`scripts/build.ps1`、`scripts/test.ps1`、`scripts/format.ps1`が実行される。

## 5. P1 認証・認可

- [x] `T-0101` ASP.NET Core Identityを導入する。
  - 参照: `docs/security-design.md`, `docs/db-design.md`
  - 完了条件: `AspNetUsers`などIdentityテーブルがMigration対象になる。

- [x] `T-0102` ApplicationUser拡張カラムを追加する。
  - 対象: `DisplayName`, `IsEnabled`, `LastLoginAt`, `CreatedAt`, `UpdatedAt`
  - 完了条件: DB設計と一致する。

- [x] `T-0103` Cookie認証設定を行う。
  - 対象: `Secure`, `HttpOnly`, `SameSite`, ログインパス
  - 完了条件: 未認証時にログインへ遷移する。

- [x] `T-0104` Admin/UserロールをSeedする。
  - 完了条件: 初期Adminユーザーでログインできる。既存Adminがいる場合はSeedがパスワードを上書きしない。

- [x] `T-0105` 認可ポリシーを実装する。
  - 対象: `RequireAdmin`, 所有者チェック
  - 完了条件: 他ユーザーのリソースアクセスが拒否される。

- [x] `T-0106` 本人退会APIと画面導線を実装する。
  - Endpoint: `DELETE /api/account`
  - 条件: 現在パスワード確認、最後のAdmin拒否、Runningジョブあり拒否
  - 完了条件: 対象ユーザーと紐づく業務データがトランザクション内で物理削除され、退会後にセッションが破棄される。

## 6. P2 DB基盤

- [x] `T-0201` 業務Entityを作成する。
  - 対象: `Articles`, `ArticleHeadings`, `ArticleGenerationJobs`, `AiGenerationLogs`, `UsageLedgers`, `SearchResults`, `XSearchPosts`, `WordpressSites`, `WordpressPosts`, `NotificationSettings`, `NotificationLogs`, `AiModelSettings`, `UserUsageLimits`, `AuditLogs`
  - 参照: `docs/db-design.md`

- [x] `T-0202` `ApplicationDbContext`を実装する。
  - 条件: Identity用DbContextを継承する。
  - 完了条件: Entity設定、Index、制約が定義される。

- [x] `T-0203` 論理削除フィルターを実装する。
  - 対象: 記事、見出し、WordPressサイト、通知設定など
  - 完了条件: 通常検索から`DeletedAt`ありの行が除外される。

- [x] `T-0204` 初期Migrationを作成する。
  - 完了条件: PostgreSQLへMigration適用できる。

- [x] `T-0205` 初期Seedを作成する。
  - 対象: Adminロール、Userロール、初期AIモデル、初期Admin
  - 完了条件: 初回起動時にログイン可能なAdminが作成される。

## 7. P3 記事管理

- [x] `T-0301` 記事一覧APIを実装する。
  - Endpoint: `GET /api/articles`
  - 条件: Userは自分の記事のみ、Adminは全記事検索可
  - 完了条件: ページング、検索、タグ絞り込みが動く。

- [x] `T-0302` 記事作成APIを実装する。
  - Endpoint: `POST /api/articles`
  - 完了条件: キーワード、タイトル、タグ、メモを保存できる。

- [x] `T-0303` 一括記事作成APIを実装する。
  - Endpoint: `POST /api/articles/bulk`
  - 完了条件: `キーワード`、`キーワード|タイトル`形式を取り込める。

- [x] `T-0304` 記事詳細、更新、論理削除APIを実装する。
  - Endpoint: `GET/PUT/DELETE /api/articles/{articleId}`
  - 完了条件: Runningジョブがある記事は削除不可。Queuedジョブは削除時にCanceledへ更新される。

- [x] `T-0305` 記事一覧画面を実装する。
  - 参照: `docs/screen-design.md`
  - 完了条件: 作成、検索、ページング、表示、投稿、削除導線がある。

- [x] `T-0306` 記事作成画面を実装する。
  - 完了条件: 画像のような入力項目と詳細設定を扱える。

- [x] `T-0307` 記事作成・一括登録でサイト別ライティング設定を選択できるようにする。
  - 条件: 選択できるのはログインユーザー所有の有効なWordPressサイトのみ。
  - 完了条件: 記事にWritingProfileWordpressSiteIdとWritingProfileSnapshotJsonが保存される。

## 8. P4 ジョブ基盤

- [x] `T-0401` ジョブ登録サービスを実装する。
  - 対象: `TitleGeneration`, `OutlineGeneration`, `BodyGeneration`, `Rewrite`, `WebSearch`, `XFullArchiveSearch`, `WordpressPost`, `Notification`
  - 完了条件: 重複ジョブ制御が動く。

- [x] `T-0402` `BackgroundService`を実装する。
  - 条件: `IDbContextFactory`または`IServiceScopeFactory`を使う。
  - 完了条件: QueuedジョブをRunningへロックして処理できる。

- [x] `T-0403` ジョブ状態APIを実装する。
  - Endpoint: `GET /api/jobs/{jobId}`
  - 完了条件: 所有者またはAdminのみ参照できる。

- [x] `T-0404` ジョブキャンセル、再試行を実装する。
  - 条件: MVPではQueuedのみキャンセル可能
  - 完了条件: Failedジョブを再試行できる。

- [x] `T-0405` ジョブ失敗記録とリトライポリシーを実装する。
  - 完了条件: 429、Timeout、認証失敗などを設計どおり分類する。

## 9. P5 AI生成

- [x] `T-0501` Geminiオプションを実装する。
  - Model: `gemini-3.8-flash`
  - Region: Japan
  - 完了条件: APIキーは環境変数またはSecretから読む。

- [x] `T-0502` Gemini Clientを実装する。
  - 参照: `docs/external-integration-design.md`
  - 完了条件: 成功、429、Timeout、認証失敗を共通結果へ変換できる。

- [x] `T-0503` タイトル候補生成を実装する。
  - 完了条件: キーワードから候補を保存または返却できる。

- [x] `T-0504` 見出し構成生成を実装する。
  - 完了条件: H2/H3階層、文字数目安、順序を保存できる。

- [x] `T-0505` 本文生成を実装する。
  - 完了条件: 見出し単位、H3以下一括生成ができる。

- [x] `T-0506` リライトを実装する。
  - 完了条件: 対象本文を更新し、元本文全文をログに出さない。

- [x] `T-0507` サイト別ライティング設定をAI生成プロンプトへ反映する。
  - 対象: タイトル候補、見出し構成、本文生成、リライト
  - 完了条件: 管理人プロフィール、語り手・キャラ設定、読者ペルソナが文体コンテキストとして使われる。

## 10. P6 検索連携

- [x] `T-0601` Tavily Clientを実装する。
  - 完了条件: 検索結果を共通DTOへ変換できる。

- [x] `T-0602` X API Full-Archive Search Clientを実装する。
  - 条件: Pay-per-use、必要時のみ、通常100件、大量調査500件
  - 完了条件: Post IDで重複排除できる。

- [x] `T-0603` 検索条件正規化とQueryHashを実装する。
  - 完了条件: 同一条件でキャッシュヒットする。

- [x] `T-0604` Tavily / XキャッシュTTLを実装する。
  - 対象: dev, staging, production, strict
  - 完了条件: 最短TTLルールが動く。

- [x] `T-0605` strict / compliance_strict判定を実装する。
  - 入力: YAMLまたはJSON辞書
  - 完了条件: legalFinanceHealthとpoliticsSafetyReputationは`compliance_strict`になる。

- [x] `T-0606` X投稿再hydrationを実装する。
  - 完了条件: production/strictでは表示・公開前に必ず再取得する。

- [x] `T-0607` 期限切れ検索キャッシュ削除Workerを実装する。
  - 完了条件: X投稿本文など短期保持データがTTL後に削除またはNULL化される。

## 11. P7 生成結果編集

- [x] `T-0701` 見出し一覧、追加、削除、並び替えAPIを実装する。
  - 完了条件: H2削除時に配下H3も処理できる。

- [x] `T-0702` 本文編集APIを実装する。
  - 完了条件: 本文履歴を作らず、見出し本文、結合本文、HTML本文の現在値を更新できる。

- [x] `T-0703` HTML変換を実装する。
  - 完了条件: H2/H3/段落へ変換できる。

- [x] `T-0704` プレビュー表示を実装する。
  - 条件: HTMLサニタイズ、XSS対策
  - 完了条件: `<script>`が実行されない。

- [x] `T-0705` 生成結果編集画面を実装する。
  - 完了条件: 画像のように左に構成、右に本文編集ができる。

## 12. P8 WordPress連携

- [x] `T-0801` WordPressサイト登録APIを実装する。
  - 条件: HTTPSのみ、SSRF対策、Application Password暗号化
  - 完了条件: レスポンスにAPP-PASSが含まれない。

- [x] `T-0802` WordPress接続テストを実装する。
  - 完了条件: 認証失敗は`success: false`として扱える。

- [x] `T-0803` WordPressカテゴリ取得を実装する。
  - MVP方針: カテゴリ一覧はDBキャッシュせず、WordPress REST APIから都度取得する。
  - 完了条件: 投稿モーダルでカテゴリ選択でき、カテゴリ一覧キャッシュテーブルを作らない。

- [x] `T-0804` WordPress投稿プレビューを実装する。
  - 完了条件: タイトル、HTML本文を確認できる。

- [x] `T-0805` WordPress投稿ジョブを実装する。
  - 条件: 投稿ステータス既定はDraft
  - 完了条件: 投稿成功時にPostId、PostUrl、PostedAtを保存する。

- [x] `T-0806` compliance_strict公開抑止を実装する。
  - 完了条件: 人間確認前はPublish不可、Draftは可。

- [x] `T-0807` 一括登録後のWordPress自動投稿を実装する。
  - 条件: 一括登録で明示的に有効化した場合のみ対象。自動投稿はDraft固定。
  - 完了条件: 本文生成とHTML変換が完了した記事ごとにWordpressPostジョブが重複なく登録される。

- [x] `T-0808` WordPressサイト別ライティング設定を実装する。
  - 対象: 管理人プロフィール、語り手・キャラ設定、読者ペルソナ
  - 完了条件: WordPressサイト登録/更新APIと設定画面で保存・編集でき、APP-PASSは引き続きレスポンスへ含まれない。

## 13. P9 Discord通知

- [x] `T-0901` 通知設定APIを実装する。
  - Provider: Discord
  - 条件: Webhook URL暗号化、レスポンス非表示

- [x] `T-0902` Discord送信Clientを実装する。
  - 完了条件: 送信成功、429、失敗を共通結果へ変換できる。

- [x] `T-0903` 送信テストを実装する。
  - 完了条件: 設定画面からテスト送信できる。

- [x] `T-0904` ジョブ完了、失敗、WordPress投稿完了通知を実装する。
  - 条件: 記事本文全文、秘密情報を通知しない。

## 14. P10 管理者機能

- [x] `T-1001` ユーザー一覧APIを実装する。
  - Endpoint: `GET /api/admin/users`
  - 完了条件: Adminのみ取得できる。

- [x] `T-1002` 管理者によるユーザー作成APIを実装する。
  - Endpoint: `POST /api/admin/users`
  - 条件: `UserManager` / `RoleManager`を使う。パスワードはレスポンス、ログ、監査ログに含めない。
  - 完了条件: UserまたはAdminロール付きでユーザーを作成できる。

- [x] `T-1003` ユーザーロール変更APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}/role`
  - 条件: 最後のAdminユーザーはUserへ降格できない。
  - 完了条件: UserをAdminへ昇格でき、監査ログに残る。

- [x] `T-1004` ユーザー更新APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}`
  - 条件: MVPでは表示名と有効/無効のみ。最後のAdminユーザーは無効化できない。
  - 完了条件: 表示名と有効状態を更新でき、監査ログに残る。

- [x] `T-1005` 利用上限更新APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}/usage-limit`
  - 完了条件: UserUsageLimitsが更新される。

- [x] `T-1006` ユーザー物理削除APIを実装する。
  - Endpoint: `DELETE /api/admin/users/{userId}`
  - 条件: 自分自身、最後のAdmin、Runningジョブありは拒否
  - 完了条件: 本人退会と共通の削除サービスで、対象ユーザーと紐づく業務データがトランザクション内で物理削除される。

- [x] `T-1007` ユーザー管理監査ログAPIを実装する。
  - Endpoint: `GET /api/admin/audit-logs`
  - 条件: 削除対象ユーザーへのFKを持たず、文字列スナップショットを保存する。
  - 完了条件: ユーザー作成、ロール変更、削除件数サマリが確認できる。

- [x] `T-1008` ユーザー管理画面を実装する。
  - 完了条件: 追加、編集、ロール変更、無効化、削除導線がある。

## 15. P11 セキュリティ

- [x] `T-1101` CSRF対策を状態変更操作へ適用する。
  - 完了条件: Tokenなし更新が拒否される。

- [x] `T-1102` SSRF対策を実装する。
  - 対象: WordPress URL、事前学習URL
  - 完了条件: localhost、private IP、metadata IPが拒否される。

- [x] `T-1103` HTMLサニタイズを実装する。
  - 完了条件: プレビューで危険タグ、イベント属性が除去される。

- [x] `T-1104` 秘密情報暗号化サービスを実装する。
  - 対象: WordPress Application Password、Discord Webhook URL
  - 完了条件: DBに平文保存されない。

- [x] `T-1105` ログマスキングを実装する。
  - 完了条件: APIキー、Webhook URL、APP-PASS、Cookie、Authorizationがログに出ない。

- [x] `T-1106` レート制限を実装する。
  - 対象: ログイン、一括登録、ジョブ登録、通知テスト、WordPress投稿
  - 完了条件: 連打が抑制される。

## 16. P12 運用

- [x] `T-1201` 本番/配置用Dockerfileを作成する。
  - 完了条件: 本番/配置用appコンテナが起動する。

- [x] `T-1202` 本番/配置用Docker Composeを作成する。
  - 対象: app, postgres, caddy
  - 条件: P0の開発用Docker Composeとは用途を分ける。
  - 完了条件: Caddy経由でappにアクセスできる。

- [x] `T-1203` Caddy設定を作成する。
  - 条件: HTTPS、HTTP to HTTPS、Reverse Proxy
  - 完了条件: Forwarded Headersが正しく動く。

- [x] `T-1204` Data Protection Key永続化を実装する。
  - 完了条件: コンテナ再作成後もCookieと暗号化データが扱える。

- [x] `T-1205` ヘルスチェックを実装する。
  - Endpoint: `/health/live`, `/health/ready`, `/health/deps`
  - 完了条件: readyでPostgreSQLとBackgroundService状態を確認できる。

- [x] `T-1206` バックアップ、リストア手順を整備する。
  - 完了条件: PostgreSQLバックアップと復元手順が確認できる。

- [x] `T-1207` 本番/配置用Docker CI確認を整備する。
  - 参照: `docs/ci-cd-design.md`, `docs/environment-setup.md`
  - 対象: 本番/配置用Dockerfile、Docker Compose、Caddy、ヘルスチェック
  - 条件: P0の開発用Docker Composeではなく、本番/配置用構成を対象にする。
  - 完了条件: main CIまたは夜間CIでDocker image build、Compose起動確認、`/health/live`確認が実行される。

## 17. P13 テスト仕上げ

- [x] `T-1301` Domain / Application単体テストを実装する。
  - 参照: `docs/test-design.md`
  - 完了条件: タグ処理、文字数計算、残数判定、strict判定が通る。

- [x] `T-1302` Infrastructure単体テストを実装する。
  - 完了条件: 外部Clientのエラー変換、URL検証、通知本文検証が通る。

- [x] `T-1303` API結合テストを実装する。
  - 完了条件: 認証、認可、記事API、Admin APIが通る。

- [x] `T-1304` DB結合テストを実装する。
  - 完了条件: Migration、Index、制約、ユーザー物理削除が通る。

- [x] `T-1305` ジョブ結合テストを実装する。
  - 完了条件: ジョブロック、状態遷移、再試行、キャンセルが通る。

- [x] `T-1306` 外部連携モックテストを実装する。
  - 完了条件: Gemini、Tavily、X、WordPress、Discordが実APIなしで検証できる。

- [x] `T-1307` セキュリティテストを実装する。
  - 完了条件: 未認証、権限不足、CSRF、XSS、SSRF、秘密情報漏えいが検証される。

- [x] `T-1308` 主要画面E2Eを実装する。
  - 対象: ログイン、記事作成、構成生成、本文生成、WordPress投稿、ユーザー削除
  - 完了条件: 主要導線がブラウザで通る。
  - [x] 一括登録の空入力・空白入力・部分成功とH2/H3バッジの回帰テストを追加し、修正前の失敗と修正後の成功、ローカルEdgeでの表示を確認する。

- [x] `T-1309` CI品質ゲートへテスト群を組み込む。
  - 参照: `docs/ci-cd-design.md`, `docs/test-design.md`
  - 対象: Unit、Integration、DB、ジョブ、外部連携モック、セキュリティ、E2E smoke
  - 条件: 通常CIではGemini、Tavily、X API、WordPress、Discordの実APIを呼ばない。
  - 完了条件: PR CIとmain CIで設計どおりのテスト範囲が実行され、失敗時に必要なテスト成果物が保存される。

- [x] `T-1310` 本人パスワード変更と共通Caddy向け本番配置を強化する。
  - 参照: `docs/api-design.md`, `docs/screen-design.md`, `docs/security-design.md`, `docs/environment-setup.md`, `docs/operation-design.md`
  - 対象: パスワード変更API/画面、app healthcheck、Migration tools profile、共通Caddy用Compose override
  - 完了条件: 正常系・現在パスワード不一致・確認不一致・ポリシー違反が結合テストで検証され、共通Caddy構成のCompose検証と公開URL経由のhealth確認手順が整備される。

- [x] `T-1311` セキュリティヘッダーとCSPを実装する。
  - 参照: `docs/security-design.md`, `docs/basic-design.md`, `docs/test-design.md`
  - 対象: `SecurityHeadersMiddleware`、`Security__ContentSecurityPolicyMode`
  - 条件: 共通Caddy構成でも欠落しないようアプリ側で付与する。CSPは既定`ReportOnly`。
  - 完了条件: `SEC-016`から`SEC-018`が結合テストで検証され、404再実行応答にもヘッダーが残る。

- [x] `T-1312` `/health/ready`の公開範囲を絞る。
  - 参照: `docs/operation-design.md`, `docs/basic-design.md`, `docs/environment-setup.md`
  - 対象: `Caddyfile`、運用手順
  - 完了条件: インターネットからは404になり、private rangeとコンテナhealthcheckからは通る。VPS上からの確認手順が文書化される。

- [x] `T-1313` NuGetとDockerイメージの脆弱性ゲートをCIへ組み込む。
  - 参照: `docs/ci-cd-design.md`
  - 対象: `scripts/scan-nuget.ps1`, `scripts/scan-image.ps1`, `.github/workflows/ci.yaml`
  - 条件: イメージスキャンはイメージ情報を外部送信しないツールを使う。対象は本番Composeがデプロイする全イメージ。
  - 条件: `--ignore-unfixed`で自動通過させない。修正版がなくても個別の受容記録を必須にする。
  - 完了条件: 受容記録のないHigh/Criticalが1件でもあればCIが失敗し、イメージのpullとビルドが`--pull`で行われ、スキャン済みイメージがそのままsmoke testに使われる。

- [x] `T-1314` 性能テストを実装し夜間CIへ組み込む。
  - 参照: `docs/test-design.md`, `docs/ci-cd-design.md`
  - 対象: `NFT-PERF-001`から`NFT-PERF-004`、`scripts/test-performance.ps1`
  - 条件: 通常のテスト実行から除外し、劣化検知目的で`continue-on-error`にする。
  - 完了条件: 記事1,000件、見出し100件、ジョブ10,000件で各基準を満たす。

- [x] `T-1315` リリース前チェックの未達項目を解消する。
  - 参照: `docs/ci-cd-design.md`, `docs/configuration-reference.md`
  - 対象: `RELEASE_NOTES.md`、`APP_IMAGE`の設定文書化、CI Migration手順の統一、開発用Composeのプロジェクト名分離
  - 完了条件: リリース前チェックの全項目が確認できる状態になる。

- [x] `T-1316` インターネット到達可能なCaddyの脆弱性を受容せず解消する。
  - 参照: `docs/ci-cd-design.md`, `security/trivy/README.md`
  - 対象: `Dockerfile.caddy`, `docker-compose.yml`, `security/trivy`
  - 条件: CVE単位で到達性を評価し、到達しうるものは受容せず修正する。
  - 完了条件: caddyイメージのHIGH/CRITICALが0件になり、受容記録は`statement`・`expired_at`・`paths`必須で検証される。

- [x] `T-1317` スキャン済み成果物と本番稼働成果物を一致させる。
  - 参照: `docs/ci-cd-design.md`, `docs/environment-setup.md`, `docs/operation-design.md`
  - 対象: 本番デプロイ手順、`scripts/scan-image.ps1`
  - 条件: CIのスキャンを本番成果物の保証として扱わない。
  - 完了条件: 本番手順から`up -d --build`が消え、VPSでも「ビルド＋スキャン → `up -d --no-build`」の順になる。共通Caddy構成ではスキャン対象からcaddyが外れる。

- [x] `T-1318` PowerShellの版差でスクリプトが壊れないようCIで担保する。
  - 参照: `docs/coding-guidelines.md`, `docs/ci-cd-design.md`
  - 対象: `scripts/check-script-encoding.ps1`, `.github/workflows/ci.yaml`
  - 条件: CIとVPSは`pwsh`、ローカル手順は`powershell`。両方で同じ結果になることを機械的に確認する。
  - 完了条件: `scripts/*.ps1`の非ASCIIとBOMがCIで拒否され、`windows-latest`上のWindows PowerShell 5.1でも受容記録の検証とセルフテストが通る。

- [x] `T-1319` `artifacts/mobile-ui-review-2026-09-16/report.md`のモバイル表示指摘に対応する。
  - 対象: 設定画面の「サイトを追加」導線、記事一覧・ユーザー管理表のモバイル表示、生成結果編集の固定ツールバー、記事削除ダイアログのフォーカス制御、一括作成プレースホルダー、`html lang`とナビゲーショントグルの名前付け。
  - 完了条件: 指摘8件のうちコード対応が必要な7件を修正し、既存E2E一式とSlopwatchが通る。管理画面をユーザーごとの詳細編集へ分割する提案は対象外（スコープ外として見送り）。

- [x] `T-1320` WordPress接続情報の復号失敗でBlazor回路がクラッシュする不具合を修正する。
  - 対象: [WordpressSiteService.CreateConnection](src/WebWritingTool.Infrastructure/Wordpress/WordpressSiteService.cs)、[Articles.razor](src/WebWritingTool.Web/Components/Pages/Articles.razor)の投稿ダイアログ。
  - 経緯: モバイルダイアログ修正の検証中に、暗号化されていない`EncryptedApplicationPassword`で`CryptographicException`が未処理のまま伝播し、投稿ダイアログを開く操作全体がクラッシュすることを発見した。
  - 完了条件: `CreateConnection`の復号失敗を`ExternalIntegrationException`（`UnauthorizedExternalApi`）へ変換し、`TestConnectionAsync`・`GetCategoriesAsync`が例外の代わりに`Failure`を返す。投稿ダイアログはカテゴリ取得失敗時に画面内で案内を表示し、既定カテゴリでの投稿は継続できる。Red/Green確認済みの結合テストを追加し、Slopwatch・結合テスト・E2E一式が通る。

- [x] `T-1321` Discord Webhook URLの復号失敗でBlazor回路がクラッシュする不具合を修正する。
  - 対象: [NotificationSettingService.ResolveDestinationAsync/SendTestAsync](src/WebWritingTool.Infrastructure/Notifications/NotificationSettingService.cs)。
  - 経緯: T-1320のWordPress側修正と同じ形の不具合。`ResolveDestinationAsync`が`SendTestAsync`のtry節の外で`Unprotect`していたため、復号失敗の`CryptographicException`が未処理のまま伝播していた。
  - 完了条件: 復号失敗を`ExternalIntegrationException`（`UnauthorizedExternalApi`）へ変換し、既存の`catch (ExternalIntegrationException)`が例外の代わりに`NotificationTestResponse(Success: false, ...)`を返す。Red/Green確認済みの結合テストを追加し、Slopwatch・単体・結合テスト全件が通る。

- [x] `T-1322` WordPress投稿ダイアログが、接続情報の認証エラー時にも「既定カテゴリで投稿できます」と誤案内する不具合を修正する。
  - 対象: [WordpressSiteService.GetCategoriesAsync](src/WebWritingTool.Infrastructure/Wordpress/WordpressSiteService.cs)、[WordpressEndpoints.ToProblemResult](src/WebWritingTool.Web/Endpoints/WordpressEndpoints.cs)、[Articles.razor](src/WebWritingTool.Web/Components/Pages/Articles.razor)の投稿ダイアログ。
  - 経緯: T-1320で追加した復号失敗の`Failure`変換により、`Articles.razor`のカテゴリ取得失敗案内が復号失敗時にも表示されるようになったが、その案内文は失敗理由を問わず末尾に「既定カテゴリで投稿できます。」と付け足していた。`WordpressPostJobHandler`は投稿時に同じ`EncryptedApplicationPassword`を`Unprotect`するため、接続情報自体が使えない場合は投稿ジョブも同じ理由で必ず失敗する。フォローアップレビューで、この案内が復号不能時にも投稿可能だと断言する誤りだと指摘された。
  - 完了条件: `WordpressServiceError`に`Unauthorized`を追加し、`GetCategoriesAsync`は`ExternalIntegrationErrorCodes.UnauthorizedExternalApi`/`ForbiddenExternalApi`の場合に`Unauthorized`を返す。`Articles.razor`は`Unauthorized`のときだけ「既定カテゴリで投稿できます」を表示せず、設定画面での再確認・再保存を促す案内に切り替える。既存の`GetCategoriesAsync`結合テストの期待値を更新し、実機相当環境（Testcontainers起動のE2Eハーネス）で案内文の切り替わりを確認する。Slopwatch・書式検証・結合テスト・単体テスト全件が通る。
  - 追記（再レビュー）: 案内文「この接続では投稿も失敗します。」は、`ForbiddenExternalApi`（403）がカテゴリ取得側だけの権限不足であり投稿権限不足とは限らない点を踏まえると断定が強すぎると指摘された。「認証または権限の問題があります。設定でサイトの登録情報と権限を確認してください。」に修正。エラー分類（`Unauthorized`が401/403をまとめて扱う判断）自体は変更していない。

- [x] `T-1323` 削除ダイアログのフォーカストラップ初期化中にキャンセルすると、閉じた後もJS初期化が続き未処理例外で回路が停止し得る不具合を修正する。
  - 対象: [Articles.razor.OnAfterRenderAsync](src/WebWritingTool.Web/Components/Pages/Articles.razor:428)、[dialogFocus.js.open](src/WebWritingTool.Web/wwwroot/js/dialogFocus.js:41)。
  - 経緯: コミット前レビューで、削除ダイアログを開いた直後（`dialogFocusModule`のJSモジュールimportを`await`している間）にキャンセルすると`deleteTarget`がnullになりダイアログがDOMから除去されるが、当時`deleteDialogFocusActive`がfalseのため閉じる分岐も実行されない。import完了後、古い処理が対象の存続を再確認せず`open`を呼ぶため、削除済みのElementReferenceがJS側でnullとなり`dialogElement.addEventListener(...)`が例外を投げる。未処理のままOnAfterRenderAsyncへ伝わり、InteractiveServerの回路を停止させ得る。レビューは実際のArticlesコンポーネントとJS transportを差し替えた再現コード（reflection経由でOnAfterRenderAsync/ConfirmDelete/CancelDeleteを直接呼び出し）で`openCalledAfterCancel=True`を実証した。
  - 完了条件: `OnAfterRenderAsync`はJSモジュールimportの`await`後、および`open`呼び出しの`await`後の両方で`deleteTarget`の存続を再確認し、nullならopen呼び出しを中止（またはclose呼び出しのみ行い）、`deleteDialogFocusActive`を立てない。`dialogFocus.js`の`open`はnull／DOM未接続の`dialogElement`を防御的に無視する。レビューの再現手法（reflectionで実コンポーネントを直接操作し、JS transportの完了タイミングを制御するテストダブル）を`tests/WebWritingTool.UnitTests/Articles/ArticlesDeleteDialogFocusTests.cs`として恒久化し、Red（修正前コードで`open`がキャンセル後に呼ばれることを確認）→Green確認済み。Slopwatch・書式検証・単体・結合・E2Eテスト全件が通る。

- [x] `T-1324` T-1323の修正自体に残っていた2件のレース（破棄済みJS参照へのclose呼び出し、import待機中の再オープンによるopen二重呼び出し）を修正する。
  - 対象: [Articles.razor.OnAfterRenderAsync/DisposeAsync](src/WebWritingTool.Web/Components/Pages/Articles.razor:430)。
  - 経緯: フォローアップレビューで、T-1323のnullチェックだけでは防げない2つの残存レースが指摘された。(1) `open`呼び出しの`await`中にキャンセル→画面遷移でDisposeAsyncが`dialogFocusModule`を破棄し、`await`完了後の新しいnull分岐が破棄済みJSObjectReferenceへ`close`を呼んで`ObjectDisposedException`を送出する（JS側のnull/isConnectedガードでは防げない、.NET側の破棄チェックが必要）。(2) import待機中にキャンセルして完了前に再度開くと、古い初期化と新しい初期化のどちらも`deleteTarget`非nullのガードを通過し、同じダイアログに`open`が2回呼ばれる（2回目の呼び出しが`previouslyFocused`を上書きし、閉じた後のフォーカス復帰先を破壊し得る）。レビューは実際のJSRuntime基底クラスを継承し本物のJSObjectReferenceを生成する再現コードで両方を実証した。
  - 完了条件: `OnAfterRenderAsync`開始時に採番する世代カウンター（`deleteDialogGeneration`）で、import・open呼び出し後に「自分が最新の初期化試行か」を再確認し、古ければJSを一切操作せず中断する。`DisposeAsync`の先頭で`disposed`フラグを立て、`OnAfterRenderAsync`内の各`await`後に確認して、破棄後は`close`を含めJS呼び出しを一切行わない。レビューの再現手法（`JSRuntime`基底クラスの薄いテストダブルで完了タイミングを制御）を模した回帰テスト2件を`ArticlesDeleteDialogFocusTests.cs`に追加し、Red（修正の世代・破棄チェック部分のみ一時的に戻し、`ObjectDisposedException`と`openCalls=2`をそれぞれ再現）→Green確認済み。Slopwatch・書式検証・単体・結合・E2Eテスト全件が通る。

- [x] `T-1325` T-1324の修正自体に残っていた2件の不具合（古い初期化が新しいダイアログをcloseする、破棄後に届いたJS参照が解放されない）を修正する。
  - 対象: [Articles.razor.OnAfterRenderAsync/DisposeAsync](src/WebWritingTool.Web/Components/Pages/Articles.razor:430)。
  - 経緯: フォローアップレビューで2件指摘された。(1) `open`呼び出しの`await`中にキャンセルして開き直すと、新しいダイアログが自身の`open`を成功させて`deleteDialogFocusActive=true`になった後、古い（世代不一致の）処理の`await`が完了し、世代不一致を理由に`close`を呼んでしまい、新しいダイアログのキー操作ハンドラーを解除する（open→open→closeとなり、.NET側は有効扱いのままJS側のEscape/Tab制御が失われる）。(2) import待機中に`DisposeAsync`が完了すると、その時点で`dialogFocusModule`はまだnullのため`DisposeAsync`は何も解放できない。import完了後に取得したJS参照は、破棄済みガードでreturnするだけで一度も解放されず、Blazorの公式JS参照解放方針に反してリークする。
  - 完了条件: 世代不一致のみを理由に`close`を呼ばないよう分岐を分離し、世代不一致では常にJSへ触れず中断する（`close`は「現世代かつdeleteTargetがnull」の場合のみ呼ぶ）。import完了直後に`disposed`を検出した場合は、`DisposeAsync`と共通の解放処理（`DisposeDialogFocusModuleAsync`、`JSDisconnectedException`を捕捉）でその場で参照を解放してからreturnする。回帰テスト2件を追加し、Red（修正部分のみ一時的に戻し、`CloseCalls=1`・`DisposeCalls=0`をそれぞれ再現）→Green確認済み。Slopwatch・書式検証・単体・結合・E2Eテスト全件が通る。

- [x] `T-1326` import待機中の再オープンで、並行取得した2件のJS参照のうち1件が未解放になる不具合を修正する。
  - 対象: [Articles.razor.OnAfterRenderAsync](src/WebWritingTool.Web/Components/Pages/Articles.razor:439)。
  - 経緯: フォローアップレビューで、`dialogFocusModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", ...)`が、import待機中にキャンセルして開き直した場合に2回独立して呼ばれることが指摘された。それぞれの呼び出しは別々の`IJSObjectReference`を返すため（公式実装で参照ごとに別IDが採番されることを確認済みとのこと）、`??=`は後から代入された側だけをフィールドに残し、先に解決した側の参照は誰にも解放されずリークする。レビューは、両方の完了に同一のモジュールを返す既存の再オープンテストではこの漏れを検出できない点も指摘した。
  - 完了条件: 進行中のimportを`Task<IJSObjectReference>`型のフィールドで共有し、`??=`で1回しか`JSRuntime.InvokeAsync`を呼ばないようにする（後発の呼び出しは同じTaskを`await`するだけで、独立した2件目のimportを発生させない）。既存の再オープンテストを、import呼び出し回数が1回のままであることを検証するように更新（Red: 修正前は2回になることを確認）。Slopwatch・書式検証・単体・結合・E2Eテスト全件が通る。

- [x] `T-1327` 外部APIなしで記事作成を確認できるダミーモードを追加する。
  - 対象: 生成Client、外部APIのDI切り替え、モード表示、開発用Compose、設定手順。
  - 完了条件: `ExternalApis__UseMocks=true` でタイトル・見出し・本文がAPIキーなしで保存でき、検索・WordPress投稿・Discord通知が実APIを呼ばない。通常モード、境界値、キャンセル、PostgreSQL保存をテストし、関連設計を更新する。

- [x] `T-1328` 検索実行・参考情報表示・生成への反映とダミーキャッシュ分離を実装する。
  - 対象: 記事編集画面の検索パネル、検索API、参考情報サービス、生成プロンプト、検索キャッシュのIsDummy区分とX投稿のスコープ付き一意制約。
  - 完了条件: Web/X検索結果を表示・生成に使え、サンプルは通常キャッシュと分離される。所有者認可、TTL、X再取得失敗、モード変更、PromptHash、実ブラウザーの一連操作を検証し、関連設計と移行手順を更新する。

- [x] `T-1329` ブラウザー検証で判明した検索対象の見出しラベルを修正する。
  - 原因: Razorの暗黙式が文字列中の`H@heading.Level`をそのまま表示していた。
  - 完了条件: 検索対象にH2/H3と見出しタイトルを表示し、回帰テストを追加する。検索・生成・キャッシュ分離のブラウザー検証を完了する。

- [x] `T-1330` 手動取得したWeb資料を自動検索結果より優先する。
  - 観測: 見出し用に手動取得した2件が、本文生成時に自動取得した新しいWeb検索10件より後ろになり、生成への採用上限から外れる場合がある。
  - 完了条件: 手動取得を永続化し、キャッシュ再利用時にも優先指定を保持する。Webの採用上限10件を維持し、手動資料の残り枠に自動結果を採用する。画面の手動取得表示、既存データ移行、再生成・上限・期限・重複・ブラウザーの回帰テストを確認する。
  - 確認: ArticleResearchApiTests 13件、ManualResearchMigrationTests 1件、ArticleResearchFlowTests 1件成功。変更C#のformat、Slopwatch、diffチェック成功。

- [x] `T-1331` ゲストログイン時にユーザー単位のモックモードを適用する。
  - 完了条件: 登録不要のゲストログイン、データ分離、CSRF保護、8時間の非永続セッションを提供する。全体モック設定がfalseでも画面・API・バックグラウンドジョブで実外部APIを呼ばず、通常ユーザーの動作を維持する。関連テストと設計書を更新する。
  - 確認: 関連単体テスト20件、GuestLogin/Account/ArticleResearch/Jobs結合テスト40件、GuestLoginFlowTests E2E 1件成功。全体テストは未実行。

- [x] `T-1332` ゲスト作成から8時間経過後に関連データを自動削除する。
  - 完了条件: 起動時・1分ごとの清掃、Cookieと保持期限の統一、Runningジョブの完了待ち、通常ユーザー・Adminの保護、トランザクションによる関連データ一括削除、境界値・失敗系・起動時のテストと設計書更新。
  - 確認: `dotnet test tests/WebWritingTool.IntegrationTests --filter 'FullyQualifiedName~GuestAccountCleanupTests|FullyQualifiedName~GuestLoginTests|FullyQualifiedName~HealthEndpointTests|FullyQualifiedName~DatabaseIntegrationTests' --no-restore` で24件成功。変更C#のformat、`dotnet slopwatch analyze -d . --exclude 'test-results/**' --fail-on warning`、diffチェック成功。全体テスト・E2Eは今回未実行。

## 17.1 UI/UXレビュー修正（2026-09-18）

- [x] `T-1333` R01/R05: 未保存変更の検知と保存・破棄・キャンセル、まとめて保存、操作単位のスコープ、近接フィードバックを実装する。
- [x] `T-1334` R02: 作成時に保存設定で構成ジョブを1件登録し、編集画面で進捗と生成結果を表示する。失敗時は同じ下書きを再利用する。
- [x] `T-1335` R03: タイトル候補・投稿・未保存確認ダイアログのフォーカス管理、Esc、起点復帰を統一する。
- [x] `T-1336` R04/R08: 本文優先、参考情報の折りたたみ、モバイル切替、長い見出し表示、プレビュー目次を整備する。
- [x] `T-1337` R06/R07: 設定の項目別エラー、説明、ゲストの連携制限表示、利用期限を改善する。
- [x] `T-1338` 共通ブランド、一覧・作成・詳細設定、削除確認、横向き表示、エラー復帰のデザイン改善を反映し、設計書・E2Eで確認する。

確認: E2E全30件、関連単体52件（Linux SDKコンテナー）成功。PC・390×844・667×375で画像確認、`git diff --check`成功。変更記録: `artifacts/reviews/ui-ux-fixes-2026-09-18.md`。

- [x] `T-1339` UI/UX改善で追加した空catch4か所によるSlopwatchのCI失敗を修正する。
  - 完了条件: コンポーネント終了時のキャンセル・回路切断をDebugログで記録し、SlopwatchとWebビルドが成功する。
  - 確認: 修正前にSW003の4件を再現。`./scripts/dotnet.ps1 slopwatch analyze -d . --exclude 'test-results/**' --fail-on warning`で0件、`./scripts/dotnet.ps1 build src/WebWritingTool.Web/WebWritingTool.Web.csproj '--property:RestoreLockedMode=true'`で警告・エラー0件。除外対象はCIに含まれないローカルの過去テスト成果物のみ。

- [x] `T-1340` Slopwatch通過後のE2Eで判明した検索パネル初期化と設定画面の幅検証を修正する。
  - 完了条件: 対話描画前の検索パネル操作を防ぎ、通知先表示を折り返す。設定のE2Eは通知先の登録とリサイズ後の幅を明示的に検証し、関連E2E・Slopwatchを確認する。
  - 確認: CIトレースで初期描画による検索パネルの閉鎖と設定画面の幅検証失敗を確認。修正後の関連E2E2件とSlopwatch、`git diff --check`が成功。

- [x] `T-1341` 一括作成の構成生成開始、見出し数引き継ぎ、不正行の入力保持を修正する。
  - 完了条件: 記事と構成生成ジョブを同時保存し、指定したH2/H3数でゲストの構成生成が完了する。全行不正は元入力を保持、部分成功は不正行のみ残して修正再送できる。PostgreSQL結合テストとE2Eで修正前の失敗・修正後の成功を確認する。
  - 確認: 修正前にジョブ未作成の結合テスト5件、入力保持・ゲスト一括生成のE2E3件の失敗を再現。修正後は関連結合21件とE2E7件成功。生成中の記事を一覧から開いた際の自動更新も確認。変更C#のformat、Slopwatch、diffチェック成功。全体テスト・実API連携テストは未実行。
  - コマンド: `dotnet test tests/WebWritingTool.IntegrationTests --no-restore --filter 'FullyQualifiedName~BulkArticleCreationTests|FullyQualifiedName~ArticleApiTests|FullyQualifiedName~DummyArticleGenerationTests|FullyQualifiedName~JobIntegrationTests'`、`dotnet test tests/WebWritingTool.E2ETests --no-restore --filter 'FullyQualifiedName~E2E003|FullyQualifiedName~GuestLoginFlowTests'`。

- [x] `T-1342` 一括登録から記事完成までの自動生成とWeb/X個別設定を実装する。
  - 完了条件: 生成範囲・検索件数・X期間を保存し、検索→未入力タイトル→構成→本文→HTMLをジョブで連携する。API省略時は従来互換を維持する。
  - 完了条件: 段階完了と後続登録の原子性、重複防止、未完了本文だけの再開、停止予約、所有者認可、生成中編集拒否、ゲストのサンプル完走を検証する。
  - 完了条件: 一覧・詳細に全体件数と記事別進捗を表示し、関連設計書・PostgreSQL結合テスト・E2Eを更新する。
  - 確認: 新規テスト10件の修正前失敗を確認後、関連単体93件・結合61件・E2E7件成功。ローカルEdgeでもゲストの本文・HTML完成と、停止後の再読み込み・構成生成の再開を確認。全体テスト・実API連携・本番反映は未実施。
  - コマンド: `dotnet test tests/WebWritingTool.UnitTests --no-restore --filter 'FullyQualifiedName~Generation|FullyQualifiedName~Jobs|FullyQualifiedName~Search|FullyQualifiedName~Rendering'`、`dotnet test tests/WebWritingTool.IntegrationTests --no-restore --filter 'FullyQualifiedName~BulkGenerationWorkflowTests|FullyQualifiedName~BulkArticleCreationTests|FullyQualifiedName~JobIntegrationTests|FullyQualifiedName~DummyArticleGenerationTests|FullyQualifiedName~ArticleResearchApiTests|FullyQualifiedName~ArticleApiTests|FullyQualifiedName~GuestAccountCleanupTests|FullyQualifiedName~WordpressPostJobHandlerIntegrationTests'`、`dotnet test tests/WebWritingTool.E2ETests --no-restore --filter 'FullyQualifiedName~E2E003|FullyQualifiedName~GuestLoginFlowTests'`。

- [x] `T-1343` 一括生成のUI/UXレビュー8件を修正する。
  - 対象: プレビュー目次、入力エラー、バッチ履歴・未完了記事の追跡、完成記事詳細、状態表現、設定の分類、タッチ領域、H2/H3の説明。
  - 完了条件: 誤遷移・エラー位置・停止記事の回帰テストを修正前に失敗させ、関連テストとPC/モバイルのブラウザ確認を通す。
  - 検証: 新規E2E 3件の失敗を先に確認し、修正後は関連E2E 19件・結合30件成功。Webビルド、Slopwatch、差分チェック成功。EdgeでPC・390px・320pxのフォーム、エラー、履歴、停止記事、完成詳細、目次を確認した。本番反映・実外部API・全テスト一式は未実施。
  - コマンド: `dotnet test tests/WebWritingTool.E2ETests --no-restore --filter 'FullyQualifiedName~BulkUx_|FullyQualifiedName~E2E003|FullyQualifiedName~GuestLoginFlowTests|FullyQualifiedName~UxReview_|FullyQualifiedName~E2E006And007'`（16件）、同プロジェクトの`--filter 'FullyQualifiedName~E2E005C|FullyQualifiedName~E2E010|FullyQualifiedName~E2E011'`（3件）、`dotnet test tests/WebWritingTool.IntegrationTests --no-restore --filter 'FullyQualifiedName~BulkGenerationWorkflowTests|FullyQualifiedName~BulkArticleCreationTests|FullyQualifiedName~ArticleApiTests'`（30件）。
  - 詳細: `artifacts/reviews/bulk-generation-ui-ux-fixes-2026-09-19.md`。

- [x] `T-1344` 記事詳細の検索説明を一括作成の自動検索と整合させる。
  - 対象: `ArticleResearchPanel.razor`の説明文。詳細画面の検索ボタンによる追加取得と、登録設定に応じた自動検索を区別する。
  - 完了条件: ビルドとブラウザ表示で、新しい説明文が表示されることを確認する。
  - 検証: `dotnet build src/WebWritingTool.Web --no-restore --nologo --verbosity minimal`（警告・エラー0件）、`git -c core.safecrlf=false diff --check`成功。Edgeでゲストの一括登録からWeb/X自動検索・構成完了後の記事詳細を開き、新しい説明とWeb/X各10件の保存結果を確認した。文言のみの変更のため自動テストの追加・再実行は行っていない。

- [x] `T-1345` Geminiの共有利用枠制御と制限待ちからの再開を実装する。
  - 完了条件: PostgreSQLでモデル別RPM・入力TPM・任意RPDと429待機を共有し、設定した安全率で送信前に制御する。日時・RetryInfo・日次QuotaFailureを扱い、秘密情報を出さない。
  - 完了条件: 待機時は試行回数を消費せず再キューし、途中まで完成した本文を再生成しない。実トークンを記録し、単体・PostgreSQL結合テストと関連設計・設定手順を更新する。
  - 検証: 日時形式Retry-After・RetryInfoの未対応を2件の失敗テストで再現後に修正。待機の二重処理も失敗を確認して修正した。関連単体77件、PostgreSQL結合34件成功。変更C#のformat、Slopwatch（0件）、Migration差分なし、`git diff --check`成功。
  - コマンド: `dotnet test tests/WebWritingTool.UnitTests --no-restore --filter 'FullyQualifiedName~Generation|FullyQualifiedName~JobRetryPolicyTests'`、`dotnet test tests/WebWritingTool.IntegrationTests --no-restore --filter 'FullyQualifiedName~GeminiQuota|FullyQualifiedName~JobIntegrationTests|FullyQualifiedName~DummyArticleGenerationTests|FullyQualifiedName~BulkGenerationWorkflowTests|FullyQualifiedName~DatabaseIntegrationTests|FullyQualifiedName~WebCompositionTests'`。
  - 設定: 実モデルのAI Studio上限は未確認。既定はRPM 5・入力TPM 100000に安全率0.8を適用し、RPDのローカル制限は未設定。全ワーカーで同じDB・Scopeを使い、利用環境では追加Migrationを適用する。実API・本番反映・全体テスト・E2Eは未実施。Batch APIと課金枠の変更は運用判断として残す。

## 18. Codex向け実装プロンプト例

### 18.1 1タスク実装

```text
todo.md の T-0301 を実装して。
関連ドキュメントは docs/api-design.md、docs/db-design.md、docs/security-design.md を確認してから進めて。
実装後は最小のテストを実行し、変更ファイルと確認コマンドを報告して。
```

### 18.2 フェーズ単位実装

```text
todo.md の P1 認証・認可を順番に実装して。
各タスク完了ごとに todo.md のチェックを更新して。
```

### 18.3 テスト修正

```text
todo.md の T-1303 API結合テストを実装して。
失敗した場合はテストログを確認して、仕様に合うように実装修正して。
```

## 19. 実装順序の推奨

1. P0 プロジェクト土台
2. P1 認証・認可
3. P2 DB基盤
4. P3 記事管理
5. P4 ジョブ基盤
6. P5 AI生成
7. P6 検索連携
8. P7 生成結果編集
9. P8 WordPress連携
10. P9 Discord通知
11. P10 管理者機能
12. P11 セキュリティ
13. P12 運用
14. P13 テスト仕上げ

## 20. 後続フェーズ候補

- 画像生成
- アイキャッチ画像作成
- 外部画像URLの保存・表示
- 画像メタデータ保存
- note投稿
- ライター管理
- 月次利用量集計
- 利用文字数の課金
- Provider別TokenCounterによるトークン事前見積もり
- WordPressメディアアップロード
- WordPress投稿時のアイキャッチ画像URL指定
- Discord以外の通知プロバイダー
- Gemini以外のAI Provider選択（OpenAI GPT、Anthropic Claudeなど）
- Workerコンテナ分離
- MFA
- CSP強制化
- CSP違反レポート収集エンドポイント（匿名書き込み経路とレポートURLの保持設計が前提）
- Secret Manager導入
- 脆弱性スキャンCI
