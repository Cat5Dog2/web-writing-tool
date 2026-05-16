# セキュリティ設計書

## 1. 目的

本書は、AIライティングツールのセキュリティ設計を定義する。
対象システムは、Blazor Web App、ASP.NET Core、PostgreSQL、EF Core、BackgroundService、Docker Compose、Caddy、VPSで構成する。

本書では、認証、認可、データ保護、外部API連携、秘密情報管理、ログ、監査、VPS運用、インシデント対応の方針を定義する。

## 2. 対象範囲

対象:

- Webアプリケーション
- APIエンドポイント
- BackgroundService
- PostgreSQL
- Docker Compose構成
- CaddyによるHTTPS終端
- Google Gemini連携
- Tavily Search API連携
- X API Full-Archive Search連携
- WordPress REST API連携
- Discord Webhook通知
- 検索キャッシュ、X投稿キャッシュ
- 監査ログ、運用ログ

MVP対象外:

- 画像生成
- ライター管理
- note投稿
- 利用文字数の課金
- 複数組織テナント管理
- SSO
- 専用WAF

## 3. セキュリティ基本方針

- すべての外部通信はHTTPSを必須とする。
- 認証済みユーザー以外は管理画面、記事、設定、ジョブへアクセスできない。
- ユーザーごとのデータ分離をアプリケーション層とDB制約で徹底する。
- 秘密情報はソースコード、ログ、APIレスポンス、画面再表示に出さない。
- 外部APIキー、WordPress Application Password、Discord Webhook URLは暗号化または安全なシークレット管理で扱う。
- 生成AI、検索API、X投稿、WordPress投稿など副作用や費用がある処理はBackgroundServiceで制御する。
- X由来データは短期保持を原則とし、表示または公開利用前に再取得する。
- `compliance_strict`記事は人間確認が完了するまで公開投稿を抑止する。
- WordPress投稿ステータスは下書きを既定値とする。

## 4. 保護対象資産

| 資産 | 例 | 主なリスク | 保護方針 |
| --- | --- | --- | --- |
| ユーザーアカウント | メールアドレス、パスワードハッシュ | なりすまし、不正ログイン | ASP.NET Core Identity、ロックアウト、HTTPS |
| 記事データ | タイトル、見出し、本文、メモ | 他ユーザー閲覧、改ざん | 所有者認可、監査ログ |
| 生成プロンプト | 追加プロンプト、事前学習テキスト | 情報漏えい | ログ出力禁止、必要最小保存 |
| 検索結果 | Tavily結果、X投稿 | 規約違反、古い情報の利用 | TTL、重複排除、再取得 |
| 外部APIキー | Gemini、Tavily、X | 不正利用、費用発生 | 環境変数またはSecret、ログ禁止 |
| WordPress認証情報 | Application Password | 不正投稿 | DB暗号化、HTTPS限定、レスポンス除外 |
| Discord Webhook URL | 通知先URL | スパム投稿、情報漏えい | 暗号化、マスク表示、ログ禁止 |
| ジョブ情報 | JobPayload、エラー | 秘密情報混入、再実行事故 | Payload最小化、マスク、冪等性 |
| 監査ログ | 操作履歴 | 改ざん、過剰保存 | 追記型、秘密情報除外 |
| バックアップ | DBダンプ | 一括漏えい | 暗号化、アクセス制限、保持期限 |

## 5. 想定脅威

| 脅威 | 内容 | 主な対策 |
| --- | --- | --- |
| 不正ログイン | パスワード総当たり、漏えいパスワード利用 | ロックアウト、強いパスワード、HTTPS |
| 水平権限昇格 | 他ユーザーの記事、設定、ジョブを閲覧・操作 | 所有者チェック、クエリ条件にUserId必須 |
| CSRF | 認証済みユーザーに意図しない変更操作を実行させる | Antiforgery Token |
| XSS | 生成本文、HTMLプレビュー、外部データ由来のスクリプト混入 | HTMLサニタイズ、出力エンコード |
| SSRF | WordPress URL、事前学習URL経由で内部ネットワークへアクセス | URL検証、プライベートIP拒否 |
| 秘密情報漏えい | ログ、レスポンス、画面、例外にキーが出る | マスク、レスポンス除外、ログフィルタ |
| 外部API乱用 | X API、Gemini、Tavilyの高額利用 | レート制限、月間安全上限、ジョブ制御 |
| 古いX投稿の利用 | 削除、非公開化、編集済み投稿を公開利用 | 公開前再hydration |
| WordPress二重投稿 | 再試行や連打による重複投稿 | 投稿履歴、冪等キー、ジョブ重複排除 |
| サプライチェーン | Dockerイメージ、NuGetの脆弱性 | 更新、脆弱性確認、最小権限 |

## 6. 認証設計

### 6.1 認証方式

MVPではASP.NET Core Identityを利用する。

- ユーザーIDは`AspNetUsers.Id`を基準にする。
- パスワードはIdentity標準のハッシュ形式で保存する。
- Cookie認証を利用する。
- 認証CookieはHTTPS前提で送信する。

### 6.2 Cookie設定

| 項目 | 方針 |
| --- | --- |
| `Secure` | 常に有効 |
| `HttpOnly` | 有効 |
| `SameSite` | `Lax`を基本。外部連携都合で必要な場合のみ個別検討 |
| 有効期限 | 運用設計で定義。長期間維持しすぎない |
| Sliding Expiration | 有効化する場合も最大有効期間を設定 |

### 6.3 ログイン保護

- パスワード失敗回数に応じてロックアウトする。
- ロックアウト回数、IP、UserAgentを監査ログへ記録する。
- ログイン失敗レスポンスでは、メールアドレス存在有無を推測できる文言を返さない。
- 管理者ユーザーはMVPでも強いパスワードを必須にする。

### 6.4 Adminユーザー追加

初期Adminユーザーは初回起動時のSeedで作成する。

- Seedは`UserManager`と`RoleManager`を使う。
- `Admin`ロールが存在しない場合は作成する。
- 既にAdminユーザーが1人以上存在する場合、Seedは新規作成もパスワード上書きもしない。
- 初期AdminのメールアドレスとパスワードはUser Secrets、環境変数、またはVPS上の秘密情報ファイルから取得する。
- Seed処理はパスワードをログ、監査ログ、例外、レスポンスに出さない。

2人目以降のAdminは、管理画面から既存ユーザーをAdminへ昇格する、または管理者が新規ユーザー作成時にAdminロールを付与する。

緊急復旧時のみ、CLIまたは一時SeedコマンドでAdminを復旧できる設計にする。ただし通常運用では使用しない。

禁止事項:

- DBへ直接`AspNetUserRoles`をINSERTしてAdminを追加する。
- 固定Adminパスワードをコードへ埋め込む。
- 起動のたびにAdminパスワードを上書きする。

### 6.5 MFA

MVPでは必須化しない。
ただし、管理者アカウントには後続フェーズでMFAを導入できる設計にする。

## 7. 認可設計

### 7.1 ロール

| ロール | 権限 |
| --- | --- |
| Admin | ユーザー管理、ユーザー削除、全体設定、ジョブ状況確認、運用監査 |
| User | 自分の記事、設定、WordPress連携、通知設定、ジョブ操作 |

MVPではライター管理は対象外とする。
Adminは他ユーザーを削除できるが、管理者自身と最後のAdminユーザーは削除できない。

### 7.2 所有者認可

以下のリソースは必ず`UserId`で所有者確認を行う。

- Articles
- ArticleHeadings
- AiGenerationJobs
- UsageLedgers
- SearchResults
- XSearchPosts
- WordpressSites
- WordpressPosts
- NotificationSettings
- AuditLogs

API、画面、BackgroundServiceのすべてで所有者整合性を確認する。
BackgroundServiceではジョブ登録時のUserIdだけを信頼せず、処理直前に関連リソースのUserIdを再確認する。

### 7.3 認可ポリシー

| ポリシー | 対象 | 条件 |
| --- | --- | --- |
| `RequireAuthenticatedUser` | 全管理画面、全API | ログイン済み |
| `RequireAdmin` | 管理者API | Adminロール |
| `AdminManageUser` | ユーザー作成、ロール変更、有効/無効、利用上限変更 | Adminロール。最後のAdminの降格、無効化は禁止 |
| `AdminDeleteUser` | ユーザー削除 | Adminロール、対象が自分自身ではない、最後のAdminではない、Runningジョブなし |
| `SelfWithdrawAccount` | 本人退会 | ログイン済み、現在パスワード確認済み、最後のAdminではない、Runningジョブなし |
| `OwnArticle` | 記事詳細、更新、削除、投稿 | Article.UserIdがログインユーザー |
| `OwnWordpressSite` | WordPress接続テスト、投稿、サイト別ライティング設定利用 | WordpressSite.UserIdがログインユーザー |
| `OwnNotificationSetting` | Discord通知設定 | NotificationSetting.UserIdがログインユーザー |
| `OwnJob` | ジョブ取得、キャンセル | AiGenerationJob.UserIdがログインユーザー |

## 8. CSRF対策

Blazor Web Appのフォーム送信、設定更新、WordPress投稿、Discord送信テスト、ジョブ登録など状態変更操作にはCSRF対策を適用する。

- Razor Components / Razor Pages / MVC / Minimal APIの構成に合わせてAntiforgery Tokenを検証する。
- GETリクエストでは状態変更しない。
- 外部Webhookの受信はMVPでは実装しない。
- APIをJavaScriptから呼ぶ場合も、認証CookieとAntiforgery Tokenをセットで検証する。

## 9. 入力検証

### 9.1 共通方針

- サーバー側で必ず検証する。
- クライアント側検証はUX向上用であり、セキュリティ境界にしない。
- 文字数、配列件数、URL形式、列挙値、ID所有者を検証する。
- エラーには内部例外、秘密情報、外部APIレスポンス全文を含めない。

### 9.2 主な入力制限

| 入力 | 制限 |
| --- | --- |
| キーワード | 最大文字数、改行数、一括登録件数を制限 |
| タイトル | 最大文字数を制限 |
| タグ | 件数、1件あたり文字数、使用可能文字を制限 |
| メモ | 最大文字数を制限 |
| 追加プロンプト | 最大3,000文字 |
| 管理人プロフィール | 最大2,000文字。プロンプト全文や通知へそのまま出さない |
| 語り手・キャラ設定 | 最大3,000文字。事実性・安全性ルールより優先しない |
| 読者ペルソナ | 最大3,000文字。個人情報や機微情報の入力に注意喚起する |
| 事前学習テキスト | 設定値で上限管理 |
| 事前学習URL | HTTPSのみ、SSRF対策必須 |
| WordPress URL | HTTPSのみ、SSRF対策必須 |
| Discord Webhook URL | Discord Webhook形式を検証 |
| X検索条件 | 最大件数、期間、言語、除外条件を検証 |

### 9.3 SSRF対策

外部URLへアプリケーションからアクセスする処理では、以下を拒否する。

- `http://`
- `file://`、`ftp://`などHTTPS以外のスキーム
- `localhost`
- `127.0.0.0/8`
- `10.0.0.0/8`
- `172.16.0.0/12`
- `192.168.0.0/16`
- `169.254.0.0/16`
- IPv6 loopback、link-local、private address
- メタデータサービスIP
- ポート番号が許可リスト外のURL

DNS解決後のIPアドレスも検証し、リダイレクト先URLにも同じ検証を適用する。

## 10. XSS / HTML対策

### 10.1 基本方針

- 画面表示では出力エンコードを基本とする。
- 生成本文、Tavily結果、X投稿、WordPressからのレスポンスを信頼しない。
- HTMLプレビューやWordPress投稿用HTMLは許可タグ方式でサニタイズする。
- `<script>`、イベントハンドラ属性、危険なURLスキームを除去する。

### 10.2 HTML本文

記事本文はWordPress投稿用にHTMLを扱うため、以下を分離する。

| 種別 | 方針 |
| --- | --- |
| 編集用本文 | ユーザー入力として保存 |
| プレビュー表示 | サニタイズ済みHTMLのみ描画 |
| WordPress投稿本文 | 投稿前にサニタイズまたは許可タグ検証 |
| ログ | 本文全文は出力しない |

### 10.3 X投稿引用

X投稿を記事に引用する場合:

- 表示またはWordPress投稿前に必ず再hydrationする。
- 削除、非公開、編集、取得不能の場合は引用を停止または人間確認に回す。
- 投稿本文、投稿者名、プロフィール情報、メディアURLはTTLに従って短期保持する。
- X Post ID / User IDは再取得、重複排除、監査用途で長期保持可能とする。

## 11. 秘密情報管理

### 11.1 秘密情報一覧

| 秘密情報 | 保存先 | 方針 |
| --- | --- | --- |
| Gemini API Key | 環境変数またはSecret | DB保存しない |
| Tavily API Key | 環境変数またはSecret | DB保存しない |
| X API Bearer Token | 環境変数またはSecret | DB保存しない |
| PostgreSQL Password | 環境変数またはSecret | ログ禁止 |
| WordPress Application Password | DB暗号化保存 | レスポンス、画面再表示禁止 |
| Discord Webhook URL | DB暗号化保存または環境変数 | レスポンス、ログ禁止 |
| ASP.NET Core Data Protection Key | 永続化ストレージ | コンテナ再作成後も復号可能にする |
| Caddy証明書情報 | Caddy管理領域 | 権限制限、バックアップ対象 |

### 11.2 開発環境

- 開発環境ではUser Secretsまたはローカル環境変数を利用する。
- `.env`、秘密情報ファイル、実APIキーはGit管理しない。
- サンプル値は実在しないダミー値のみ使用する。

### 11.3 本番環境

- VPS上の環境変数または権限制限された秘密情報ファイルで管理する。
- Docker Composeへ渡す値は最小限にする。
- `docker inspect`、プロセス一覧、ログに秘密情報が出ないようにする。
- 退職、漏えい疑い、誤公開、外部API異常利用時は即時ローテーションする。

### 11.4 DB暗号化

WordPress Application PasswordとDiscord Webhook URLはDB暗号化保存する。

方針:

- アプリケーション層で暗号化してから保存する。
- 復号は外部送信直前のみ行う。
- 復号後の値をログ、例外、監査ログへ出さない。
- 画面再表示ではマスク表示のみとする。
- 暗号鍵はData Protection Keyなどの永続化された鍵管理を使う。

## 12. 外部連携セキュリティ

### 12.1 共通方針

- `IHttpClientFactory`を利用する。
- タイムアウト、リトライ、レート制限を設定する。
- 外部APIレスポンス全文をログへ出さない。
- エラーは内部コードへ正規化する。
- 秘密情報はAuthorizationヘッダーまたは必要最小限の送信項目に限定する。
- APIキーはリクエストURLのクエリ文字列に含めない。

### 12.2 Google Gemini

| 項目 | 方針 |
| --- | --- |
| Provider | Google Gemini |
| Model | Google Gemini 3.1 Pro Preview |
| API Model ID | `gemini-3.1-pro-preview` |
| Region | Japan |
| 実行主体 | BackgroundService |

セキュリティ要件:

- APIキーは環境変数またはSecretで管理する。
- プロンプト全文はログへ出さない。
- 外部送信する入力には、記事作成に不要なユーザー情報や秘密情報を含めない。
- サイト別ライティング設定は外部AIへ送信されるため、保存画面でその旨を示し、秘密情報や第三者の機微情報を入力させない。
- Geminiからの出力は信頼せず、HTML表示前にサニタイズする。
- モデルエラー、レート制限、タイムアウトはジョブ失敗として記録する。

### 12.3 Tavily Search API

セキュリティ要件:

- APIキーは環境変数またはSecretで管理する。
- 検索条件は正規化し、`QueryHash`でキャッシュ判定する。
- キャッシュ有効時は外部APIを呼ばない。
- URL、タイトル、取得日時、ドメイン名は監査、重複排除、再取得用メタデータとして保持する。
- 本文、要約、スニペットはTTL満了後に削除または再取得する。
- 検索結果由来のHTMLやテキストは表示時にエンコードする。

### 12.4 X API Full-Archive Search

契約と取得方針:

| 項目 | 方針 |
| --- | --- |
| 契約 | Pay-per-use |
| 実行条件 | 必要時のみ |
| `max_results`通常 | 100 |
| `max_results`大量調査時 | 500 |
| 月間安全上限 | 10,000から50,000 posts程度から開始 |
| Post ID | 長期保持可 |
| 投稿本文 | 6から24時間 |
| 公開前 | 必ず再取得 |

セキュリティ要件:

- X API Bearer Tokenは環境変数またはSecretで管理する。
- 検索期間、言語、除外条件、最大件数を必ず指定できるようにする。
- 同じ投稿はX Post IDで重複排除する。
- 投稿本文、投稿者名、プロフィール情報、メディアURLは短期保持する。
- Post ID / User IDは再取得、重複排除、監査用途で長期保持できる。
- X由来の集計データは、個別投稿を復元できない形で保持する。
- 表示またはWordPress投稿前に再hydrationし、削除、非公開、編集、取得不能を確認する。

### 12.5 WordPress REST API

セキュリティ要件:

- WordPress URLはHTTPSのみ許可する。
- Application PasswordはDB暗号化保存する。
- 接続テスト、カテゴリ取得、投稿時に所有者確認を行う。
- 投稿ステータスの既定値は`Draft`とする。
- `Publish`指定時でも`HumanReviewRequired = true`の場合は公開投稿を抑止する。
- 投稿失敗時にApplication Password、認証ヘッダー、外部APIレスポンス全文を保存しない。
- 同一記事の重複投稿を防止するため、投稿履歴または冪等キーで制御する。

### 12.6 Discord Webhook

セキュリティ要件:

- Discord Webhook URLはDB暗号化保存または環境変数で管理する。
- 画面表示ではマスクする。
- ログ、レスポンス、監査ログへWebhook URLを出さない。
- 通知本文にはAPIキー、Application Password、Webhook URL、プロンプト全文、記事本文全文を含めない。
- 通知には記事ID、ジョブID、ステータス、短いエラー概要、管理画面URLを含める。
- レート制限、送信失敗、429を扱い、必要に応じて再試行する。

## 13. 検索キャッシュとTTL

### 13.1 環境別TTL

| 環境 | Tavily検索結果JSON | Tavily本文・要約・スニペット | X投稿生データ | X ID | X表示・公開前 |
| --- | --- | --- | --- | --- | --- |
| `dev` | 24時間 | 24時間 | 6時間 | 長期保持可 | 任意 |
| `staging` | 6時間 | 24時間 | 6時間 | 長期保持可 | 任意 |
| `production` | 24時間 | 7日 | 24時間 | 長期保持可 | 必ず再取得 |
| `strict` | 24時間 | 24時間 | 1時間 | 長期保持可 | 必ず再取得 |

### 13.2 TTL決定ルール

TTLは以下の単位で短縮できる。

- 環境単位
- ユーザー単位
- 記事単位
- データソース単位
- トピック単位

環境単位はシステム全体の上限であり必ず優先する。
その他の単位は厳しくする方向の上書きのみ許可する。
最終TTLは候補のうち最も短い値を採用する。

### 13.3 strict判定

| モード | 対象 | セキュリティ方針 |
| --- | --- | --- |
| `normal` | 一般SEO記事、ハウツー、技術メモ、ブログ下書き、evergreen記事 | 通常TTL |
| `strict` | 最新情報、ニュース、X投稿利用、料金比較、API料金、商品価格、在庫、ランキング、口コミ、SaaS比較 | strict TTL |
| `compliance_strict` | 医療、法律、税金、投資、保険、政治、選挙、災害、事件、個人や企業の不祥事 | strict TTL + 人間確認必須 |

`compliance_strict`はMVPでは`strict + HumanReviewRequired`として扱う。

### 13.4 辞書メンテナンス

| 項目 | 方針 |
| --- | --- |
| 担当 | 運営者本人 |
| 更新頻度 | 月1回 + 誤判定に気づいた時 |
| 更新対象 | strict判定キーワード、除外キーワード、トピックカテゴリ、TTLポリシー |
| 管理形式 | YAMLまたはJSON |
| 反映条件 | テストケース追加、判定結果確認後 |

## 14. ジョブ処理セキュリティ

### 14.1 基本方針

- ジョブ登録時と処理時の両方で認可を確認する。
- JobPayloadには秘密情報を入れない。
- 外部送信直前に必要な秘密情報を復号する。
- 使用後に復号済み値を保持しない。
- ジョブ失敗時のエラーには秘密情報と本文全文を含めない。

### 14.2 ジョブ別注意点

| ジョブ | 注意点 |
| --- | --- |
| TitleGeneration | プロンプト全文をログに出さない |
| OutlineGeneration | Tavily / XキャッシュTTLを適用 |
| BodyGeneration | X引用がある場合は公開前再hydration必須 |
| Rewrite | 元本文全文をログに出さない |
| WebSearch | SSRF対策、検索条件正規化 |
| XFullArchiveSearch | 最大件数、月間安全上限、重複排除 |
| WordpressPost | 所有者確認、Draft既定、二重投稿防止。一括自動投稿はDraftのみ |
| Notification | Webhook URLマスク、本文全文通知禁止 |

### 14.3 冪等性

副作用のあるジョブでは冪等性を確保する。

- WordPress投稿は`ArticleId`、`WordpressSiteId`、`RequestedAt`、投稿履歴で重複を制御する。
- 一括作成からのWordPress自動投稿は`Articles.AutoPostQueuedAt`と既存`WordpressPost`ジョブ/履歴を確認し、同一記事を複数回投稿しない。
- Discord通知は通知種別とジョブIDを組み合わせて重複送信を抑制する。
- X検索は検索条件ハッシュとPost IDで重複取得を抑制する。

## 15. DBセキュリティ

### 15.1 EF Core

- EF CoreのLINQとパラメータ化クエリを基本とする。
- Raw SQLを使う場合は必ずパラメータ化する。
- ユーザー入力をSQL文字列へ直接連結しない。
- クエリには所有者UserId条件を含める。

### 15.2 PostgreSQL

- PostgreSQLは外部公開しない。
- Docker Composeネットワーク内からのみ接続可能にする。
- 本番DBパスワードは強固なランダム値にする。
- DBユーザーはアプリに必要な権限に限定する。
- バックアップファイルはアクセス権を制限する。

### 15.3 削除方針

通常の記事削除、WordPressサイト削除、通知設定削除は監査可能性を考慮し、`DeletedAt`による論理削除を基本とする。

本人退会または管理者がユーザーを削除する場合は例外的に物理削除とし、対象ユーザーに紐づく業務データもトランザクション内で物理削除する。

ユーザー物理削除の制約:

- 管理者APIでは管理者自身を削除できない。本人退会APIでは本人のみ削除できる。
- 最後のAdminユーザーは削除できない。
- 対象ユーザーに`Running`ジョブがある場合は削除できない。
- 本人退会では現在パスワードの再確認を必須にする。
- 削除前に対象ユーザーのWordPress Application PasswordとDiscord Webhook URLを復号しない。
- 対象ユーザーが操作ユーザーの既存監査ログは物理削除し、削除監査ログだけをFKなしのスナップショットで残す。
- 削除監査ログには削除実行者、対象ユーザーIDのスナップショット、削除件数サマリを記録する。
- 削除監査ログは削除対象ユーザーへのFKを持たない。

## 16. ログ設計

### 16.1 出力してよい情報

- リクエストID
- ユーザーID
- 記事ID
- ジョブID
- 外部連携種別
- ステータス
- 処理時間
- エラーコード
- マスク済みエラー概要

### 16.2 出力禁止情報

- APIキー
- Bearer Token
- WordPress Application Password
- Discord Webhook URL
- DBパスワード
- 認証Cookie
- CSRF Token
- プロンプト全文
- 記事本文全文
- X投稿本文の長期ログ
- 外部APIレスポンス全文

### 16.3 ログマスキング

以下の形式はログ出力前にマスクする。

- `Authorization`ヘッダー
- `api_key`
- `access_token`
- `bearer`
- `password`
- `application_password`
- `webhook`
- URL内のトークンらしき値

## 17. 監査ログ設計

### 17.1 記録対象イベント

| イベント | 記録内容 |
| --- | --- |
| ログイン成功/失敗 | UserId、IP、UserAgent、結果 |
| ログアウト | UserId、日時 |
| 管理者によるユーザー作成 | AdminUserId、TargetUserId、付与ロール、結果 |
| 管理者によるロール変更 | AdminUserId、TargetUserId、変更前ロール、変更後ロール、結果 |
| WordPressサイト登録/更新/削除 | UserId、SiteId、操作 |
| 管理者によるユーザー削除 | AdminUserId、TargetUserIdSnapshot、削除件数サマリ、結果 |
| 本人退会 | TargetUserIdSnapshot、削除件数サマリ、結果。削除対象ユーザーへのFKは持たない |
| WordPress接続テスト | UserId、SiteId、結果 |
| WordPress投稿 | UserId、ArticleId、SiteId、PostStatus、結果 |
| Discord通知設定更新 | UserId、Provider、操作 |
| Discord送信テスト | UserId、結果 |
| AIモデル設定変更 | AdminUserId、Provider、Model |
| strict辞書更新 | AdminUserId、更新対象、バージョン |
| 人間確認完了 | UserId、ArticleId、TopicRisk |
| ジョブキャンセル | UserId、JobId、JobType |

### 17.2 監査ログの禁止事項

- 秘密情報を保存しない。
- 記事本文全文を保存しない。
- X投稿本文を保持期限を超えて保存しない。
- 外部APIレスポンス全文を保存しない。

## 18. CORS / CSP / セキュリティヘッダー

### 18.1 CORS

MVPでは同一オリジン利用を基本とし、CORSは原則無効にする。
外部フロントエンドを分離する場合のみ、許可オリジンを明示する。

### 18.2 セキュリティヘッダー

CaddyまたはASP.NET Core Middlewareで以下を付与する。

| ヘッダー | 方針 |
| --- | --- |
| `Strict-Transport-Security` | 本番HTTPSで有効 |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `X-Frame-Options` | `DENY`またはCSP `frame-ancestors 'none'` |
| `Content-Security-Policy` | 段階導入。インラインスクリプトを減らす |

### 18.3 CSP方針

MVP初期はレポートモードで導入し、画面確認後に強制へ移行する。

基本方針:

- `default-src 'self'`
- 画像は必要な外部ドメインのみ許可
- API通信は自ドメインと必要な外部APIのみ許可
- `frame-ancestors 'none'`
- 危険なインラインスクリプト依存を増やさない

## 19. Docker / VPS / Caddy設計

### 19.1 ネットワーク

- 外部公開ポートはCaddyの80/443のみとする。
- ASP.NET CoreアプリはDocker内部ネットワークに限定する。
- PostgreSQLは外部公開しない。
- VPSのファイアウォールで不要ポートを閉じる。

### 19.2 Caddy

- TLS証明書はCaddyで自動管理する。
- HTTPはHTTPSへリダイレクトする。
- 逆プロキシ時はForwarded Headersを正しくアプリへ渡す。
- アップロードやリクエストボディサイズは上限を設定する。

### 19.3 ASP.NET Core Forwarded Headers

Caddy配下でHTTPS判定、リダイレクトURL、Cookie Secure判定が崩れないように、Forwarded Headersを設定する。

検証対象:

- `X-Forwarded-For`
- `X-Forwarded-Proto`
- 既知プロキシまたは既知ネットワーク

### 19.4 コンテナ

- 本番コンテナでは開発用設定を使わない。
- 不要なツールを含めない。
- アプリは非root実行を検討する。
- イメージ更新時は脆弱性情報を確認する。
- Data Protection Keyとアップロード領域は永続化する。

## 20. ヘルスチェック

### 20.1 エンドポイント

| 種別 | 公開範囲 | 内容 |
| --- | --- | --- |
| Liveness | 外部監視可 | アプリプロセス稼働 |
| Readiness | 内部または管理者限定 | DB接続、BackgroundService状態 |
| Dependency | 管理者限定 | Gemini、Tavily、X、WordPress、Discordの疎通状況 |

### 20.2 出力制限

ヘルスチェックには以下を含めない。

- 接続文字列
- APIキー
- Webhook URL
- Application Password
- 外部APIレスポンス全文

## 21. バックアップとリストア

- DBバックアップは暗号化またはアクセス制限された場所へ保存する。
- バックアップには暗号化済み秘密情報が含まれるため、Data Protection Keyの扱いも管理する。
- バックアップ保持期間は運用設計に従う。
- リストア手順では、復号鍵、DBパスワード、環境変数を安全に復旧する。
- リストア後は外部連携の接続テストを実施する。

## 22. データ保持と削除

### 22.1 検索データ

| データ | 保持方針 |
| --- | --- |
| Tavily検索結果JSON | 環境別TTLに従う |
| Tavily本文・要約・スニペット | 環境別TTLに従う |
| Tavily URL、タイトル、取得日時、ドメイン名 | 30から180日。MVP既定90日 |
| X投稿本文、投稿者名、プロフィール情報、メディアURL | 最大24時間。strictは1時間 |
| X Post ID / User ID | 長期保持可 |
| X集計データ | 30から180日程度 |

### 22.2 削除処理

- 期限切れ検索キャッシュは定期ジョブで削除する。
- X投稿生データはTTL満了後に削除または本文をNULL化する。
- 削除処理の結果は件数のみログへ記録する。
- 削除済み本文を監査ログやジョブログから復元できないようにする。

## 23. レート制限と利用上限

### 23.1 API / 画面

- ログイン試行にレート制限を設ける。
- 一括記事登録の件数に上限を設ける。
- ジョブ登録頻度に上限を設ける。
- WordPress投稿、Discord送信テストの連打を防止する。

### 23.2 外部API

| 連携 | 上限方針 |
| --- | --- |
| Gemini | 同時実行数、タイムアウト、リトライ回数を制限 |
| Tavily | キャッシュ優先、検索条件正規化 |
| X API | 通常100件、大量調査500件、月間10,000から50,000 posts安全上限 |
| WordPress | 同一記事の二重投稿防止 |
| Discord | 通知種別とジョブIDで重複送信抑制 |

## 24. エラー応答設計

- ユーザー向けには原因を特定しすぎない安全なメッセージを返す。
- 管理者向けにはエラーコード、ジョブID、外部連携種別を表示する。
- 詳細なスタックトレースは本番レスポンスに含めない。
- 外部APIの認証エラー時もキーや認証ヘッダーを表示しない。

例:

| ケース | ユーザー向け |
| --- | --- |
| WordPress認証失敗 | WordPress接続に失敗しました。設定を確認してください。 |
| Discord通知失敗 | 通知送信に失敗しました。Webhook設定を確認してください。 |
| Gemini失敗 | AI生成に失敗しました。時間をおいて再実行してください。 |
| X API上限 | X検索の取得上限に達したため、条件を絞って再実行してください。 |

## 25. セキュリティテスト観点

| ID | 観点 | 期待結果 |
| --- | --- | --- |
| SEC-001 | 未ログインで記事一覧へアクセス | ログイン画面へ遷移または401 |
| SEC-002 | 他ユーザーの記事IDを指定 | 404または403 |
| SEC-003 | 他ユーザーのWordPressサイトIDで投稿 | 403 |
| SEC-004 | CSRF Tokenなしで設定更新 | 拒否 |
| SEC-005 | 生成本文に`<script>`混入 | 表示時に実行されない |
| SEC-006 | WordPress URLにlocalhost指定 | 拒否 |
| SEC-007 | 事前学習URLにprivate IP指定 | 拒否 |
| SEC-008 | Application Password登録後の取得API | 平文が含まれない |
| SEC-009 | Discord Webhook URL取得API | 平文が含まれない |
| SEC-010 | ログにAPIキーを含む例外発生 | マスクされる |
| SEC-011 | X引用あり記事のWordPress公開投稿 | 公開前再hydrationされる |
| SEC-012 | `compliance_strict`記事のPublish指定 | 人間確認前は拒否 |
| SEC-013 | WordPress投稿ステータス未指定 | Draftになる |
| SEC-014 | X API `max_results`に501以上指定 | 拒否または500へ丸める |
| SEC-015 | X API月間安全上限超過見込み | 停止または管理者確認 |

## 26. インシデント対応

### 26.1 秘密情報漏えい疑い

1. 該当キー、Webhook、Application Passwordを無効化またはローテーションする。
2. 関連ログと監査ログで利用範囲を確認する。
3. 外部APIの利用量、WordPress投稿履歴、Discord送信履歴を確認する。
4. 必要に応じてユーザーへ通知する。
5. 再発防止としてログマスク、権限、保存場所を見直す。

### 26.2 不正ログイン疑い

1. 該当ユーザーを一時無効化する。
2. セッションを失効させる。
3. パスワードリセットを実施する。
4. WordPress Application PasswordとDiscord Webhook URLの悪用有無を確認する。
5. IP、UserAgent、操作履歴を確認する。

### 26.3 Xデータ保持違反疑い

1. X投稿生データの保持期限超過有無を確認する。
2. 期限切れデータを削除または本文NULL化する。
3. 公開記事に利用されたX引用を再検証する。
4. TTL設定、削除ジョブ、strict判定を修正する。

### 26.4 WordPress誤投稿

1. WordPress側で投稿を下書き化または削除する。
2. `WordpressPosts`履歴を確認する。
3. `HumanReviewRequired`、Draft既定、二重投稿防止が動作したか確認する。
4. 必要に応じて該当WordPress Application Passwordをローテーションする。

## 27. MVP完了条件

MVPでは以下を満たす。

- 未認証ユーザーが管理画面とAPIへアクセスできない。
- 他ユーザーの記事、ジョブ、WordPress設定、通知設定へアクセスできない。
- 初期AdminはSeedで作成され、既存Adminのパスワードを上書きしない。
- 2人目以降のAdminは管理画面から追加または昇格でき、監査ログに記録される。
- Adminユーザーは他ユーザーを削除でき、対象ユーザーに紐づく業務データも物理削除される。
- ユーザーは現在パスワード確認後に本人退会でき、対象ユーザーに紐づく業務データも物理削除される。
- 管理者APIでの自分自身削除、最後のAdminユーザー削除、Runningジョブを持つユーザー削除はできない。本人退会でも最後のAdminユーザーとRunningジョブありは拒否する。
- CSRF対策が状態変更操作に適用されている。
- WordPress Application PasswordとDiscord Webhook URLが平文保存、平文レスポンス、平文ログ出力されない。
- Gemini、Tavily、X APIキーがDB保存されず、ログへ出ない。
- WordPress URL、事前学習URLにSSRF対策が適用されている。
- HTMLプレビューでスクリプトが実行されない。
- X投稿生データが環境別TTLで期限切れになる。
- productionとstrictではX投稿の表示・公開前に再取得される。
- `compliance_strict`記事は人間確認前にWordPress公開投稿できない。
- WordPress投稿ステータス未指定時はDraftになる。
- Discord通知本文に秘密情報と記事本文全文が含まれない。
- PostgreSQLが外部公開されていない。
- CaddyでHTTPSが有効である。
- 監査ログに主要な設定変更、投稿、通知、strict辞書更新が記録される。

## 28. 残課題

| 項目 | 方針 |
| --- | --- |
| MFA | MVP後に管理者から導入を検討 |
| CSP強制化 | MVP初期はReport-Onlyで確認し、安定後に強制 |
| Secret Manager | VPS単体運用では環境変数または秘密情報ファイル、本格運用時に専用Secret Manager検討 |
| 暗号鍵ローテーション | 初期は手順化、後続で自動化を検討 |
| WAF | MVPではCaddyとアプリ側対策を基本とし、必要に応じて導入 |
| 脆弱性スキャン | CIまたは定期運用でNuGet、Dockerイメージ確認を追加 |
