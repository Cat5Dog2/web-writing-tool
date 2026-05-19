# データ保持・プライバシー設計書

## 1. 目的

本書は、AIライティングツールで扱うデータの分類、保存場所、保持期限、削除・匿名化、ログ出力、バックアップ内データの扱いを定義する。

対象は、ユーザーアカウント、記事、AI生成履歴、Tavily検索結果、X投稿キャッシュ、WordPress連携情報、Discord通知情報、監査ログ、運用ログ、バックアップである。

## 2. 基本方針

- ユーザーごとのデータ分離を前提とし、すべての業務データに所有者を持たせる。
- 秘密情報、認証情報、Webhook URL、外部APIキーをログ、レスポンス、監査ログへ出さない。
- プロンプト全文、記事本文全文、X投稿本文は長期ログへ出さない。
- X由来データは短期保持を原則とし、表示または公開利用前に再取得する。
- Tavily / XのキャッシュはTTLに従って削除または本文をNULL化する。
- 通常の業務削除は監査可能性を考慮し、`DeletedAt`による論理削除を基本とする。
- 本人退会と管理者によるユーザー削除は例外として物理削除する。
- バックアップには暗号化済み秘密情報が含まれるため、本番データと同じ保護対象として扱う。

## 3. データ分類

| 分類 | 例 | 保護方針 |
| --- | --- | --- |
| 認証情報 | パスワードハッシュ、認証Cookie、CSRF Token | Identity標準、HTTPS、ログ禁止 |
| システム秘密情報 | DBパスワード、Gemini API Key、Tavily API Key、X Bearer Token | 環境変数またはSecret、DB保存禁止、ログ禁止 |
| ユーザー別秘密情報 | WordPress Application Password、Discord Webhook URL | DB暗号化保存、平文レスポンス禁止、復号は送信直前のみ |
| ユーザー入力本文 | 記事タイトル、本文、メモ、追加プロンプト、サイト別ライティング設定 | 所有者認可、ログ全文禁止、必要最小保存 |
| AI生成データ | 見出し、本文、HTML本文、生成ログ、利用文字数 | 所有者認可、生成ログは概要のみ |
| 外部由来データ | Tavily検索結果、X投稿本文、投稿者情報、URL | TTL、再取得、表示時エンコード |
| 操作履歴 | 監査ログ、投稿履歴、通知ログ、ジョブログ | 秘密情報除外、必要最小限の識別子と結果 |
| 運用データ | アプリログ、Caddyログ、PostgreSQLログ | ローテーション、秘密情報マスク |
| バックアップ | DBダンプ、Data Protectionキー、Caddyデータ | アクセス制限、世代管理、外部退避 |

## 4. 保存場所

| データ | 保存先 | 備考 |
| --- | --- | --- |
| ユーザーアカウント | `AspNetUsers` とIdentity関連テーブル | Identity標準形式 |
| 記事・見出し | `Articles`, `ArticleHeadings` | 通常削除は論理削除 |
| AI生成概要 | `AiGenerationLogs` | プロンプト全文は保存しない |
| 利用文字数履歴 | `UsageLedgers` | 加算専用台帳 |
| ジョブ状態 | `ArticleGenerationJobs` | Payload / Resultに秘密情報を入れない |
| Tavily検索結果 | `SearchResults` | 種別ごとの期限カラムを持つ |
| X投稿検索結果 | `XSearchPosts` | 生データは短期保持 |
| WordPress設定 | `WordpressSites` | Application Passwordは暗号化保存 |
| WordPress投稿履歴 | `WordpressPosts` | 認証情報は保存しない |
| Discord通知設定 | `NotificationSettings` | Webhook URLは暗号化保存 |
| 通知履歴 | `NotificationLogs` | 送信先はマスク済みのみ |
| 監査ログ | `AuditLogs` | 秘密情報、本文全文を保存しない |
| 運用ログ | Docker / Caddy / PostgreSQLログ | ローテーション対象 |
| バックアップ | VPS内、外部ストレージ | アクセス制限対象 |

MVPでは画像生成、アイキャッチ画像、外部画像URL、画像メタデータを保存しない。

## 5. 保持期限

### 5.1 業務データ

| データ | 通常保持 | 通常削除 | ユーザー物理削除時 |
| --- | --- | --- | --- |
| ユーザーアカウント | アカウント有効期間中 | 無効化または本人退会 | 物理削除 |
| 記事 | ユーザーが保持する間 | `DeletedAt`による論理削除 | 物理削除 |
| 見出し・本文 | 記事に従う | `DeletedAt`による論理削除 | 物理削除 |
| HTML本文 | 記事に従う | 記事と同じ | 物理削除 |
| サイト別ライティング設定スナップショット | 記事に従う | 記事と同じ | 物理削除 |
| ジョブ履歴 | MVPでは自動削除しない | 後続で古い成功ジョブのアーカイブを検討 | 物理削除 |
| AI生成ログ | MVPでは自動削除しない | 概要のみ保持 | 物理削除 |
| 利用文字数台帳 | MVPでは自動削除しない | 補正は負の行を追加 | 物理削除 |
| WordPressサイト | ユーザーが保持する間 | `DeletedAt`による論理削除 | 物理削除 |
| WordPress投稿履歴 | 履歴として保持 | 通常は保持 | 物理削除 |
| 通知設定 | ユーザーが保持する間 | `DeletedAt`による論理削除 | 物理削除 |
| 通知ログ | 履歴として保持 | 通常は保持 | 物理削除 |
| 監査ログ | MVPでは自動削除しない | 秘密情報なしで保持 | 対象ユーザーが操作した既存ログは物理削除 |

### 5.2 Tavily / Xキャッシュ

| データ | dev | staging | production | strict |
| --- | --- | --- | --- | --- |
| Tavily検索結果JSON | 24時間 | 6時間 | 24時間 | 24時間 |
| Tavily本文・要約・スニペット | 24時間 | 24時間 | 7日 | 24時間 |
| X投稿生データ | 6時間 | 6時間 | 24時間 | 1時間 |
| X Post ID / User ID | 長期保持可 | 長期保持可 | 長期保持可 | 長期保持可 |
| X表示・公開前再取得 | 任意 | 推奨 | 必須 | 必須 |

補足:

- Tavily URL、タイトル、取得日時、ドメイン名は30から180日保持できる。MVP既定は90日とする。
- X由来の集計データは30から180日保持できる。MVP既定は90日とする。
- X Post ID / User IDは再取得、重複排除、監査用途で長期保持できる。
- X投稿本文、投稿者名、プロフィール情報、メディアURLはTTL満了後に削除またはNULL化する。

### 5.3 運用ログとバックアップ

| データ | 保持期限 |
| --- | --- |
| Dockerログ | 14日から30日 |
| Caddyアクセスログ | 14日から30日 |
| PostgreSQLログ | 14日から30日 |
| 日次DBバックアップ | 7日 |
| 週次DBバックアップ | 4週 |
| 月次DBバックアップ | 3か月 |
| Data Protectionキー | アプリ運用期間中保持、バックアップ対象 |
| Caddy証明書データ | Caddy管理に従い保持、バックアップ対象 |

## 6. TTL決定ルール

TTLは以下の候補のうち最も短い値を採用する。

1. 環境単位TTL
2. ユーザー単位TTL
3. 記事単位TTL
4. データソース単位TTL
5. トピック単位TTL

環境単位TTLより長くする上書きは無効とする。ユーザー、記事、データソース、トピック単位の指定は、より厳しくする方向のみ有効とする。

| 判定 | 対象 | 扱い |
| --- | --- | --- |
| `normal` | 一般SEO記事、ハウツー、技術メモ、evergreen記事 | 通常TTL |
| `strict` | 最新情報、ニュース、X投稿利用、料金比較、価格、在庫、ランキング、口コミ、SaaS比較 | strict TTL |
| `compliance_strict` | 医療、法律、税金、投資、保険、政治、選挙、災害、事件、不祥事 | strict TTL + 人間確認必須 |

`compliance_strict`はMVPでは`strict + HumanReviewRequired`として扱い、人間確認前のWordPress公開投稿を抑止する。下書き投稿は可能とする。

## 7. 削除・匿名化方針

### 7.1 論理削除

通常削除では以下を論理削除する。

- `Articles`
- `ArticleHeadings`
- `WordpressSites`
- `NotificationSettings`

論理削除済みデータは通常の一覧、検索、選択肢から除外する。監査、投稿履歴、復旧調査のため、関連履歴は原則保持する。

### 7.2 ユーザー物理削除

本人退会または管理者によるユーザー削除では、対象ユーザーに紐づく業務データをトランザクション内で物理削除する。

削除対象:

- `NotificationLogs`
- `WordpressPosts`
- `XSearchPosts`
- `SearchResults`
- `UsageLedgers`
- `AiGenerationLogs`
- 対象ユーザーが操作した既存`AuditLogs`
- `ArticleGenerationJobs`
- `ArticleHeadings`
- `Articles`
- `WordpressSites`
- `NotificationSettings`
- `UserUsageLimits`
- Identity関連テーブル
- `AspNetUsers`

制約:

- 対象ユーザーに`Running`ジョブがある場合は削除しない。
- 最後のAdminユーザーは削除しない。
- 管理者自身の管理者API削除は拒否する。
- 本人退会では現在パスワード確認を必須にする。
- 削除前に対象ユーザーのWordPress Application PasswordやDiscord Webhook URLを復号しない。

削除監査ログ:

- 削除実行者、対象ユーザーIDスナップショット、削除件数サマリ、結果を記録する。
- 削除対象ユーザーへのFKを持たない。
- 本人退会の場合は`UserId`をNULLにし、対象ユーザーIDを文字列スナップショットとして保存する。

### 7.3 期限切れキャッシュ削除

期限切れ検索キャッシュ削除ジョブは以下を行う。

- `SearchResults.RawJsonExpiresAt`を過ぎた検索結果JSONを削除またはNULL化する。
- `SearchResults.ContentExpiresAt`を過ぎた本文、要約、スニペットを削除またはNULL化する。
- `XSearchPosts.ContentExpiresAt`を過ぎた本文、投稿者名、プロフィール情報、メディアURLを削除またはNULL化する。
- メタデータ保持期限を過ぎた行は削除対象にする。
- 削除結果は件数のみログへ記録する。

削除済み本文やX投稿生データを監査ログ、ジョブログ、運用ログから復元できないようにする。

## 8. AI生成データの扱い

AI生成時に外部Providerへ送信する内容には、記事本文、追加プロンプト、事前学習テキスト、サイト別ライティング設定スナップショットが含まれ得る。

方針:

- プロンプト全文はアプリログ、ジョブログ、通知、監査ログへ出さない。
- `AiGenerationLogs`にはProvider、Model、Operation、PromptHash、文字数、応答時間、成否、ErrorCodeを保存する。
- `UsageLedgers`には文字数利用履歴を保存する。
- `ArticleGenerationJobs.PayloadJson`と`ResultJson`には秘密情報と本文全文を入れない。
- 外部APIレスポンス全文は保存しない。
- 生成本文は記事・見出しの現在値として保存し、生成ログを本文履歴や復元用途に使わない。

## 9. 外部連携データの扱い

### 9.1 Tavily

- 検索条件は正規化し、`QueryHash`でキャッシュ判定する。
- URL、タイトル、取得日時、ドメイン名は重複排除と再取得用メタデータとして保持できる。
- 本文、要約、スニペットはTTL満了後に削除または再取得する。
- 表示時は出力エンコードする。

### 9.2 X API

- X投稿はPost IDで重複排除する。
- 投稿本文、投稿者名、プロフィール情報、メディアURLは短期保持する。
- Post ID、User ID、個別投稿を復元できない集計データは長期保持できる。
- productionとstrictでは、表示またはWordPress投稿前に必ず再hydrationする。
- 削除、非公開、編集、取得不能の場合は引用を停止するか、人間確認へ回す。

### 9.3 WordPress

- WordPress URLはHTTPSのみ許可する。
- Application PasswordはDB暗号化保存し、画面再表示、レスポンス、ログ、監査ログへ出さない。
- カテゴリ一覧はMVPではDBキャッシュしない。
- 投稿履歴にはPost ID、Post URL、投稿ステータス、エラー概要を保存する。
- 投稿失敗時にApplication Password、認証ヘッダー、外部APIレスポンス全文を保存しない。

### 9.4 Discord

- Discord Webhook URLはDB暗号化保存し、画面ではマスク表示のみとする。
- 通知本文には秘密情報、プロンプト全文、記事本文全文を含めない。
- 通知ログには送信先マスク、イベント種別、成否、短いメッセージ概要、ErrorCodeを保存する。

## 10. ログ・監査ログ

### 10.1 運用ログ

出力してよい情報:

- `traceId`
- `userId`
- `articleId`
- `jobId`
- `provider`
- `eventName`
- `statusCode`
- `elapsedMs`
- `errorCode`
- 削除件数

出力禁止情報:

- APIキー
- Bearer Token
- DBパスワード
- WordPress Application Password
- Discord Webhook URL
- Cookie
- Authorizationヘッダー
- CSRF Token
- プロンプト全文
- 記事本文全文
- X投稿本文の長期ログ
- 外部APIレスポンス全文

### 10.2 監査ログ

記録対象:

- ログイン成功/失敗
- ユーザー作成、ロール変更、有効/無効変更
- 本人退会
- 管理者によるユーザー削除
- WordPressサイト登録、更新、削除
- WordPress接続テスト、投稿
- Discord通知設定更新、送信テスト
- AIモデル設定変更
- strict辞書更新
- 人間確認完了

監査ログには秘密情報、パスワード、Webhook URL、Application Password、記事本文全文、プロンプト全文、外部APIレスポンス全文を保存しない。

## 11. バックアップとリストア

バックアップ対象:

- PostgreSQL
- Data Protectionキー保存先
- Caddy証明書データ
- Compose設定

方針:

- DBバックアップには暗号化済みApplication Password、暗号化済みWebhook URL、記事本文、監査ログが含まれる。
- バックアップは本番DBと同じ機密度で扱う。
- VPS内バックアップだけに依存せず、外部ストレージへ退避する。
- バックアップファイルのアクセス権を制限する。
- リストア時はData Protectionキー、DBパスワード、環境変数を安全に復旧する。
- リストア後は外部連携の接続テストを実施する。

保持期限:

| 種別 | 保持 |
| --- | --- |
| 日次バックアップ | 7日 |
| 週次バックアップ | 4週 |
| 月次バックアップ | 3か月 |

## 12. 禁止事項

- `.env`、秘密情報ファイル、実APIキーをGit管理する。
- DBへGemini、Tavily、X APIキーを保存する。
- WordPress Application PasswordとDiscord Webhook URLを平文保存する。
- Application Password、Webhook URL、Bearer Tokenをレスポンスへ含める。
- パスワード、APIキー、Webhook URL、Application Passwordをログや監査ログへ出す。
- プロンプト全文、記事本文全文、外部APIレスポンス全文を運用ログへ出す。
- X投稿生データをTTL超過後も本文として保持する。
- 削除対象ユーザーの秘密情報を復号してからユーザー削除する。
- 本番DB、本番APIキー、実WordPress、実Discordを通常の自動テストで使用する。

## 13. 実装時の確認項目

- `DeletedAt`論理削除フィルターが対象Entityに適用されている。
- ユーザー物理削除サービスが削除順序を明示し、DBカスケードだけに依存していない。
- ユーザー物理削除前に`Running`ジョブ、最後のAdmin、本人確認条件を検証している。
- 削除監査ログが削除対象ユーザーへのFKを持たない。
- `SearchResults`と`XSearchPosts`に期限カラムがある。
- 期限切れ検索キャッシュ削除ジョブが件数のみログ出力する。
- X投稿本文、投稿者名、プロフィール情報、メディアURLがTTL後に削除またはNULL化される。
- productionとstrictでX表示・公開前再hydrationが必須になっている。
- `compliance_strict`記事が人間確認前にPublishできない。
- `AiGenerationLogs`がプロンプト全文を保存しない。
- `ArticleGenerationJobs.PayloadJson`と`ResultJson`に秘密情報と本文全文を入れていない。
- WordPress Application PasswordとDiscord Webhook URLが暗号化保存される。
- レスポンスDTOに秘密情報が含まれない。
- ログマスキングがAPIキー、Bearer Token、Cookie、Webhook URL、Application Passwordを対象にしている。
- バックアップ手順にData Protectionキー保存先が含まれている。

## 14. テスト観点

| ID | 観点 | 期待結果 |
| --- | --- | --- |
| `DRP-001` | 記事論理削除 | 通常一覧から除外される |
| `DRP-002` | 本人退会 | 対象ユーザーの業務データが物理削除され、Cookieが破棄される |
| `DRP-003` | 管理者ユーザー削除 | 対象ユーザーの業務データが物理削除される |
| `DRP-004` | Runningジョブありユーザー削除 | 409で拒否される |
| `DRP-005` | 最後のAdmin削除 | 400で拒否される |
| `DRP-006` | Tavily TTL満了 | 本文・要約・スニペットが削除またはNULL化される |
| `DRP-007` | X投稿生データTTL満了 | 投稿本文などが削除またはNULL化される |
| `DRP-008` | X公開前再取得 | production/strictで再hydrationされる |
| `DRP-009` | AI生成ログ | プロンプト全文が保存されない |
| `DRP-010` | 通知ログ | Webhook URLと記事本文全文が保存されない |
| `DRP-011` | WordPress設定取得 | Application Passwordがレスポンスに含まれない |
| `DRP-012` | ログマスキング | APIキー、Webhook URL、Application Password、Cookieが出力されない |
| `DRP-013` | バックアップ手順 | PostgreSQL、Data Protectionキー、Caddyデータが対象に含まれる |

## 15. 関連ドキュメント

- [要件定義書](requirements.md)
- [DB設計書](db-design.md)
- [API設計書](api-design.md)
- [外部連携設計書](external-integration-design.md)
- [トピックリスク分類設計書](topic-risk-classification.md)
- [セキュリティ設計書](security-design.md)
- [ジョブ設計書](job-design.md)
- [運用設計書](operation-design.md)
- [テスト設計書](test-design.md)
