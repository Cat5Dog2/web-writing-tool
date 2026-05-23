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
- Google Gemini 3.5 Flashによるテキスト生成Clientが実装される。
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
  - Model: `gemini-3.5-flash`
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

- [ ] `T-0601` Tavily Clientを実装する。
  - 完了条件: 検索結果を共通DTOへ変換できる。

- [ ] `T-0602` X API Full-Archive Search Clientを実装する。
  - 条件: Pay-per-use、必要時のみ、通常100件、大量調査500件
  - 完了条件: Post IDで重複排除できる。

- [ ] `T-0603` 検索条件正規化とQueryHashを実装する。
  - 完了条件: 同一条件でキャッシュヒットする。

- [ ] `T-0604` Tavily / XキャッシュTTLを実装する。
  - 対象: dev, staging, production, strict
  - 完了条件: 最短TTLルールが動く。

- [ ] `T-0605` strict / compliance_strict判定を実装する。
  - 入力: YAMLまたはJSON辞書
  - 完了条件: legalFinanceHealthとpoliticsSafetyReputationは`compliance_strict`になる。

- [ ] `T-0606` X投稿再hydrationを実装する。
  - 完了条件: production/strictでは表示・公開前に必ず再取得する。

- [ ] `T-0607` 期限切れ検索キャッシュ削除Workerを実装する。
  - 完了条件: X投稿本文など短期保持データがTTL後に削除またはNULL化される。

## 11. P7 生成結果編集

- [ ] `T-0701` 見出し一覧、追加、削除、並び替えAPIを実装する。
  - 完了条件: H2削除時に配下H3も処理できる。

- [ ] `T-0702` 本文編集APIを実装する。
  - 完了条件: 本文履歴を作らず、見出し本文、結合本文、HTML本文の現在値を更新できる。

- [ ] `T-0703` HTML変換を実装する。
  - 完了条件: H2/H3/段落へ変換できる。

- [ ] `T-0704` プレビュー表示を実装する。
  - 条件: HTMLサニタイズ、XSS対策
  - 完了条件: `<script>`が実行されない。

- [ ] `T-0705` 生成結果編集画面を実装する。
  - 完了条件: 画像のように左に構成、右に本文編集ができる。

## 12. P8 WordPress連携

- [ ] `T-0801` WordPressサイト登録APIを実装する。
  - 条件: HTTPSのみ、SSRF対策、Application Password暗号化
  - 完了条件: レスポンスにAPP-PASSが含まれない。

- [ ] `T-0802` WordPress接続テストを実装する。
  - 完了条件: 認証失敗は`success: false`として扱える。

- [ ] `T-0803` WordPressカテゴリ取得を実装する。
  - MVP方針: カテゴリ一覧はDBキャッシュせず、WordPress REST APIから都度取得する。
  - 完了条件: 投稿モーダルでカテゴリ選択でき、カテゴリ一覧キャッシュテーブルを作らない。

- [ ] `T-0804` WordPress投稿プレビューを実装する。
  - 完了条件: タイトル、HTML本文を確認できる。

- [ ] `T-0805` WordPress投稿ジョブを実装する。
  - 条件: 投稿ステータス既定はDraft
  - 完了条件: 投稿成功時にPostId、PostUrl、PostedAtを保存する。

- [ ] `T-0806` compliance_strict公開抑止を実装する。
  - 完了条件: 人間確認前はPublish不可、Draftは可。

- [ ] `T-0807` 一括登録後のWordPress自動投稿を実装する。
  - 条件: 一括登録で明示的に有効化した場合のみ対象。自動投稿はDraft固定。
  - 完了条件: 本文生成とHTML変換が完了した記事ごとにWordpressPostジョブが重複なく登録される。

- [ ] `T-0808` WordPressサイト別ライティング設定を実装する。
  - 対象: 管理人プロフィール、語り手・キャラ設定、読者ペルソナ
  - 完了条件: WordPressサイト登録/更新APIと設定画面で保存・編集でき、APP-PASSは引き続きレスポンスへ含まれない。

## 13. P9 Discord通知

- [ ] `T-0901` 通知設定APIを実装する。
  - Provider: Discord
  - 条件: Webhook URL暗号化、レスポンス非表示

- [ ] `T-0902` Discord送信Clientを実装する。
  - 完了条件: 送信成功、429、失敗を共通結果へ変換できる。

- [ ] `T-0903` 送信テストを実装する。
  - 完了条件: 設定画面からテスト送信できる。

- [ ] `T-0904` ジョブ完了、失敗、WordPress投稿完了通知を実装する。
  - 条件: 記事本文全文、秘密情報を通知しない。

## 14. P10 管理者機能

- [ ] `T-1001` ユーザー一覧APIを実装する。
  - Endpoint: `GET /api/admin/users`
  - 完了条件: Adminのみ取得できる。

- [ ] `T-1002` 管理者によるユーザー作成APIを実装する。
  - Endpoint: `POST /api/admin/users`
  - 条件: `UserManager` / `RoleManager`を使う。パスワードはレスポンス、ログ、監査ログに含めない。
  - 完了条件: UserまたはAdminロール付きでユーザーを作成できる。

- [ ] `T-1003` ユーザーロール変更APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}/role`
  - 条件: 最後のAdminユーザーはUserへ降格できない。
  - 完了条件: UserをAdminへ昇格でき、監査ログに残る。

- [ ] `T-1004` ユーザー更新APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}`
  - 条件: MVPでは表示名と有効/無効のみ。最後のAdminユーザーは無効化できない。
  - 完了条件: 表示名と有効状態を更新でき、監査ログに残る。

- [ ] `T-1005` 利用上限更新APIを実装する。
  - Endpoint: `PUT /api/admin/users/{userId}/usage-limit`
  - 完了条件: UserUsageLimitsが更新される。

- [ ] `T-1006` ユーザー物理削除APIを実装する。
  - Endpoint: `DELETE /api/admin/users/{userId}`
  - 条件: 自分自身、最後のAdmin、Runningジョブありは拒否
  - 完了条件: 本人退会と共通の削除サービスで、対象ユーザーと紐づく業務データがトランザクション内で物理削除される。

- [ ] `T-1007` ユーザー管理監査ログAPIを実装する。
  - Endpoint: `GET /api/admin/audit-logs`
  - 条件: 削除対象ユーザーへのFKを持たず、文字列スナップショットを保存する。
  - 完了条件: ユーザー作成、ロール変更、削除件数サマリが確認できる。

- [ ] `T-1008` ユーザー管理画面を実装する。
  - 完了条件: 追加、編集、ロール変更、無効化、削除導線がある。

## 15. P11 セキュリティ

- [ ] `T-1101` CSRF対策を状態変更操作へ適用する。
  - 完了条件: Tokenなし更新が拒否される。

- [ ] `T-1102` SSRF対策を実装する。
  - 対象: WordPress URL、事前学習URL
  - 完了条件: localhost、private IP、metadata IPが拒否される。

- [ ] `T-1103` HTMLサニタイズを実装する。
  - 完了条件: プレビューで危険タグ、イベント属性が除去される。

- [ ] `T-1104` 秘密情報暗号化サービスを実装する。
  - 対象: WordPress Application Password、Discord Webhook URL
  - 完了条件: DBに平文保存されない。

- [ ] `T-1105` ログマスキングを実装する。
  - 完了条件: APIキー、Webhook URL、APP-PASS、Cookie、Authorizationがログに出ない。

- [ ] `T-1106` レート制限を実装する。
  - 対象: ログイン、一括登録、ジョブ登録、通知テスト、WordPress投稿
  - 完了条件: 連打が抑制される。

## 16. P12 運用

- [ ] `T-1201` 本番/配置用Dockerfileを作成する。
  - 完了条件: 本番/配置用appコンテナが起動する。

- [ ] `T-1202` 本番/配置用Docker Composeを作成する。
  - 対象: app, postgres, caddy
  - 条件: P0の開発用Docker Composeとは用途を分ける。
  - 完了条件: Caddy経由でappにアクセスできる。

- [ ] `T-1203` Caddy設定を作成する。
  - 条件: HTTPS、HTTP to HTTPS、Reverse Proxy
  - 完了条件: Forwarded Headersが正しく動く。

- [ ] `T-1204` Data Protection Key永続化を実装する。
  - 完了条件: コンテナ再作成後もCookieと暗号化データが扱える。

- [ ] `T-1205` ヘルスチェックを実装する。
  - Endpoint: `/health/live`, `/health/ready`, `/health/deps`
  - 完了条件: readyでPostgreSQLとBackgroundService状態を確認できる。

- [ ] `T-1206` バックアップ、リストア手順を整備する。
  - 完了条件: PostgreSQLバックアップと復元手順が確認できる。

- [ ] `T-1207` 本番/配置用Docker CI確認を整備する。
  - 参照: `docs/ci-cd-design.md`, `docs/environment-setup.md`
  - 対象: 本番/配置用Dockerfile、Docker Compose、Caddy、ヘルスチェック
  - 条件: P0の開発用Docker Composeではなく、本番/配置用構成を対象にする。
  - 完了条件: main CIまたは夜間CIでDocker image build、Compose起動確認、`/health/live`確認が実行される。

## 17. P13 テスト仕上げ

- [ ] `T-1301` Domain / Application単体テストを実装する。
  - 参照: `docs/test-design.md`
  - 完了条件: タグ処理、文字数計算、残数判定、strict判定が通る。

- [ ] `T-1302` Infrastructure単体テストを実装する。
  - 完了条件: 外部Clientのエラー変換、URL検証、通知本文検証が通る。

- [ ] `T-1303` API結合テストを実装する。
  - 完了条件: 認証、認可、記事API、Admin APIが通る。

- [ ] `T-1304` DB結合テストを実装する。
  - 完了条件: Migration、Index、制約、ユーザー物理削除が通る。

- [ ] `T-1305` ジョブ結合テストを実装する。
  - 完了条件: ジョブロック、状態遷移、再試行、キャンセルが通る。

- [ ] `T-1306` 外部連携モックテストを実装する。
  - 完了条件: Gemini、Tavily、X、WordPress、Discordが実APIなしで検証できる。

- [ ] `T-1307` セキュリティテストを実装する。
  - 完了条件: 未認証、権限不足、CSRF、XSS、SSRF、秘密情報漏えいが検証される。

- [ ] `T-1308` 主要画面E2Eを実装する。
  - 対象: ログイン、記事作成、構成生成、本文生成、WordPress投稿、ユーザー削除
  - 完了条件: 主要導線がブラウザで通る。

- [ ] `T-1309` CI品質ゲートへテスト群を組み込む。
  - 参照: `docs/ci-cd-design.md`, `docs/test-design.md`
  - 対象: Unit、Integration、DB、ジョブ、外部連携モック、セキュリティ、E2E smoke
  - 条件: 通常CIではGemini、Tavily、X API、WordPress、Discordの実APIを呼ばない。
  - 完了条件: PR CIとmain CIで設計どおりのテスト範囲が実行され、失敗時に必要なテスト成果物が保存される。

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
- Secret Manager導入
- 脆弱性スキャンCI
