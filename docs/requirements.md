# 要件定義書

## 1. 概要

本書は、Blazor Web App、ASP.NET Core、PostgreSQL、EF Core、BackgroundService、Docker Compose、Caddy、VPS を用いて、AI記事作成・管理・WordPress投稿を行うWebアプリケーションの要件を定義する。

主機能は「記事作成」であり、キーワード入力から見出し構成、本文生成、記事編集、WordPress投稿、通知までを一連のワークフローとして扱う。画像生成はMVPには含めず、後続フェーズで対応する。

## 2. 前提・方針

- 利用者はログイン済みユーザーとしてアプリを利用する。
- MVPでは日本語記事作成を主対象とし、将来的に多言語対応できる設計にする。
- AI生成処理、Web検索、X投稿検索、WordPress投稿、通知送信など時間のかかる処理はバックグラウンドジョブとして扱う。
- 初期構成は単一VPS上のDocker Compose運用とし、CaddyでHTTPS終端とリバースプロキシを行う。
- .NETは新規開発時点の安定版を採用する。2026年5月時点の想定は .NET 10 / ASP.NET Core 10 とする。
- UIはBlazor Web Appを採用し、初期表示はSSR、操作が多い画面はInteractive Serverを基本とする。

## 3. 技術スタック

| 区分 | 採用技術 | 用途 |
| --- | --- | --- |
| UI | Blazor Web App | 管理画面、フォーム、記事編集、一覧操作 |
| Backend | ASP.NET Core | 認証、画面、API、DI、設定、ヘルスチェック |
| ORM | EF Core | PostgreSQLへのデータアクセス、マイグレーション |
| Database | PostgreSQL | ユーザー、記事、ジョブ、生成履歴、外部連携設定の永続化 |
| Background | BackgroundService | AI生成、Web検索、X投稿検索、投稿、通知などの非同期処理 |
| Container | Docker Compose | Webアプリ、DB、Caddyのローカル/本番起動 |
| Reverse Proxy | Caddy | HTTPS、リバースプロキシ、静的ファイル配信補助 |
| Hosting | VPS | 単一サーバー運用、バックアップ、ログ管理 |

## 4. 利用者・権限

| ロール | 説明 | 主な操作 |
| --- | --- | --- |
| 管理者 | システム全体を管理するユーザー | MVPではユーザー管理、利用上限設定、ユーザー管理監査ログ。AIモデル設定、外部連携一括管理は後続フェーズ |
| 一般ユーザー | 記事を作成・管理するユーザー | 記事作成、編集、投稿、通知設定 |
| ライター | 後続フェーズで追加する記事編集担当ユーザー | 割り当て記事の閲覧、編集、納品ステータス更新 |

MVPでは管理者と一般ユーザーを実装対象とし、ライター管理は実装しない。

## 5. 画面要件

### 5.1 共通レイアウト

- 上部にロゴ、ユーザーメニュー、主要ナビゲーションを表示する。
- 主要ナビゲーションは以下を想定する。
  - キーワード発掘
  - リサーチ
  - 記事作成
  - 画像生成
  - リライト
- 画面右上付近に文字数上限設定、モデル名、残り構成数などの利用上限情報を表示する。
- 利用文字数は履歴として記録する。MVPでは月次集計、残り文字数、課金額としての表示や制御には使用しない。
- フッターにサービス名、コピーライトを表示する。

### 5.2 記事作成: プロジェクト一覧

画像の「プロジェクト」タブに相当する画面。

- 記事一覧を作成日時順に表示する。
- 一覧に以下の情報を表示する。
  - 作成日
  - 見出し取得方法または検索モード
  - ステータス
  - 記事タイトル
  - タグ
  - メモ
  - 操作ボタン
- 操作ボタンは以下を持つ。
  - 表示
  - 投稿
  - 削除
- 検索条件として、タグ検索、タイトル/キーワード/メモ検索を提供する。
- ページングを提供する。
- 「記事を作成」「一括作成」「記事インポート」ボタンを提供する。

### 5.3 記事作成: 詳細

画像の「詳細」タブに相当する画面。

- キーワード入力欄を表示する。
- タイトル入力欄を表示する。
- 「記事タイトル候補を出す」ボタンを提供する。
- アイキャッチ画像はMVPでは作成しない。外部画像URLの保存・表示、画像ファイル保存、画像メタデータ保存も提供しない。
- 詳細設定の表示/非表示を切り替えられる。
- 詳細設定には以下を含める。
  - H2の個数
  - H3の個数
  - 文章のトーン
  - 情報を日本国内に限定するチェック
  - タグ
  - メモ
  - サジェストキーワード
  - 関連キーワード
  - 事前学習の入力種別: テキスト / URL
  - 事前学習本文
  - 追加プロンプト
  - サイト別ライティング設定
- 下部設定には以下を含める。
  - 見出し構築方法
  - 作成対象モデルまたは作成モード
  - 検索モード
  - 通知設定
- 「構成を作成」ボタンで見出し構成生成ジョブを登録する。

### 5.4 記事作成: 生成結果編集

画像の生成済み記事編集画面に相当する。

- 左側にメタディスクリプション、見出し構成を表示する。
- 右側に選択中の見出し本文または記事本文の編集エリアを表示する。
- MVPではアイキャッチ画像の作成、画像に文字を入れる機能、再生成を提供しない。
- MVPでは外部画像URLの保存・表示、画像ファイル保存、画像メタデータ保存も提供しない。
- 記事本文には以下の操作を提供する。
  - 再取得
  - 下階層を要約
  - 要約
  - 長文化
  - リライト
  - 句点で改行を挿入
- 見出し構成では以下を操作できる。
  - H2/H3の追加
  - H2/H3の削除
  - 並び替え
  - Web検索の適用
  - 文字数目安の表示
- 記事全体に対して以下の操作を提供する。
  - 一括取得
  - H3以下一括取得
  - HTML変換
  - WordPress投稿
  - 保存

### 5.5 一括登録モーダル

画像の「一括登録」モーダルに相当する。

- 複数キーワードを改行区切りで入力できる。
- タイトルも指定する場合は、キーワードの後に `|` を入力してタイトルを指定できる。
- 以下の一括設定を指定できる。
  - H2の個数
  - H3の個数
  - 情報を日本国内に限定する
  - タイトル構築方法
  - 見出し構築方法
  - 作成対象モデル
  - 検索モード
  - サイト別ライティング設定
  - WordPress自動投稿の有効/無効
  - 自動投稿先WordPressサイト
  - 自動投稿カテゴリ
- 登録実行後、記事作成ジョブを複数作成する。
- WordPress自動投稿が有効な場合、本文生成とHTML変換が完了した記事から順にWordPress下書き投稿ジョブを自動作成する。

### 5.6 記事作成: 設定

画像の「設定」タブに相当する画面。

- 通知用のDiscord Webhook URLを登録できる。
- 通知送信テストを実行できる。
- WordPress連携サイトを登録・更新・削除できる。
- WordPress連携では以下を管理する。
  - サイト名
  - URL
  - ユーザーID
  - Application Password
  - 投稿カテゴリ
  - 管理人プロフィール
  - 語り手・キャラ設定
  - 読者ペルソナ
- 記事作成時にWordPressサイトを選択した場合、そのサイトの管理人プロフィール、語り手・キャラ設定、読者ペルソナをもとにタイトル候補、見出し構成、本文、リライトを生成する。
- 一括登録でWordPress自動投稿を有効にし、サイト別ライティング設定が未指定の場合は、自動投稿先サイトのライティング設定を使用する。
- 高度な設定を管理する。
  - 記事を日本語に統一する
  - 自動で本文に改行を追加する
- 事前学習設定を管理する。
  - 文字数
  - 個数
- 本文文字数の目安を管理する。
- 記事作成ステータスを確認できる。
- ライター管理はMVPでは表示しない。

### 5.7 WordPress投稿モーダル

画像の「WP投稿」モーダルに相当する。

- 投稿タイトルを編集できる。
- 投稿本文をHTML形式で編集できる。
- Markdown変換、note投稿導線はMVPでは提供しない。
- 登録済みWordPressサイトを選択して投稿できる。
- WordPress投稿ステータスは下書きをデフォルトとする。
- 投稿結果としてWordPressの記事URL、投稿ID、エラー内容を保存する。

## 6. 機能要件

### 6.1 認証・アカウント

- ユーザー認証はASP.NET Core Identityを使用する。
- ユーザーはメールアドレスとパスワードでログインできる。
- 初期Adminユーザーは初回起動時のSeedで作成する。
- 2人目以降のAdminユーザーは、管理画面から既存ユーザーをAdminへ昇格する、または管理者が新規ユーザーを作成してAdminロールを付与する。
- 管理者はユーザーの作成、有効/無効、ロール、利用上限を管理できる。
- ユーザーは現在パスワードを再確認したうえで本人退会できる。
- 本人退会時は対象ユーザーに紐づく記事、見出し、ジョブ、検索結果、X投稿キャッシュ、WordPress連携、通知設定、利用履歴、対象ユーザーが操作した既存監査ログなどの業務データを物理削除する。
- 本人退会時に`Running`ジョブがある場合は退会を拒否する。
- 最後のAdminユーザーは本人退会できない。
- 管理者は他のユーザーを削除できる。
- 最後のAdminユーザーの降格、無効化、削除は拒否する。
- 管理者がユーザーを削除する場合、そのユーザーに紐づく記事、見出し、ジョブ、検索結果、X投稿キャッシュ、WordPress連携、通知設定、利用履歴、対象ユーザーが操作した既存監査ログなどの業務データも物理削除する。
- 管理者自身の削除と最後のAdminユーザーの削除は拒否する。
- Cookieベース認証を基本とし、認可はサーバー側で必ず検証する。

### 6.2 利用上限・文字数管理

- ユーザーごとに月間または契約単位の利用可能文字数設定を保存できる。ただしMVPでは消費量集計とは連動しない。
- AI生成、追加プロンプト、事前学習入力などの入力・出力文字数を記録する。
- MVPでは月次利用量集計、残り文字数算出、利用文字数の課金換算を実装しない。
- MVPではProvider別の文字数/トークン換算とトークン事前見積もりを実装しない。
- MVPでは文字数消費に基づく上限超過判定は行わない。構成生成回数など明示的な残数がある場合のみ、生成前に確認してジョブ登録を制御する。
- モデル別に消費量を記録できる。

### 6.3 記事管理

- 記事は下書き、構成作成待ち、構成作成中、構成作成済み、本文生成待ち、本文生成中、完了、投稿済み、失敗のステータスを持つ。
- 記事にはキーワード、タイトル、タグ、メモ、本文、HTML本文、メタディスクリプションを保存する。
- MVPでは記事本文の履歴管理、差分表示、過去版復元は実装しない。本文、HTML本文、見出し本文は現在値のみ保存する。
- 記事は検索、ページング、削除ができる。
- 記事単体の削除は論理削除を基本とする。ただし、管理者によるユーザー削除時は紐づく記事も物理削除する。

### 6.4 見出し構成生成

- キーワード、タイトル、H2/H3数、見出し構築方法、検索モード、事前学習、追加プロンプトを元に構成を生成する。
- 見出しは階層構造として保存する。
- 各見出しに文字数目安、検索適用有無、生成ステータスを持たせる。
- ユーザーは生成後に見出しを追加、削除、並び替えできる。

### 6.5 本文生成

- 見出し単位または記事全体で本文を生成できる。
- H3以下一括生成をサポートする。
- 生成済み本文に対して再取得、要約、長文化、リライトを行える。
- 生成ジョブの成否と利用情報は履歴として保存し、失敗時の再実行に備える。生成済み本文の過去版保存はMVPに含めない。

### 6.6 タイトル候補生成

- キーワードから複数のタイトル候補を生成できる。
- 候補から1つを選択して記事タイトルに反映できる。
- 生成履歴を保存する。

### 6.7 事前学習・リサーチ

- 事前学習はテキストまたはURLで入力できる。
- URL指定時は本文抽出、要約、AIプロンプトへの反映を行う。
- Web検索モードではTavily Search APIから検索結果を取得し、見出し構成や本文生成の根拠として利用する。
- キーワードに関するX投稿情報はX API Full-Archive Searchから取得する。
- X API Full-Archive Searchでは検索条件を絞り、期間、言語、除外条件、最大件数を設定できるようにする。
- Tavily検索結果とX投稿検索結果はキャッシュし、同一クエリ・条件の再取得を抑制する。
- X投稿は外部投稿IDで重複排除し、同じ投稿を再取得・再保存しない。
- X投稿の本文、投稿者名、プロフィール情報、メディアURLなどの生データは最大24時間保持を原則とする。
- X Post ID、User ID、個別投稿を復元できない集計データは長期保持できる。
- 生成済み記事内でX投稿を引用する場合、公開前に投稿の削除、非公開化、編集の有無を再検証する。
- 検索キャッシュ保持期間は環境別に上書きできる。
- productionではTavily検索結果JSONを24時間、Tavily本文・要約・スニペットを7日、X投稿生データを24時間保持する。
- strictではTavily本文・要約・スニペットを24時間、X投稿生データを1時間保持する。
- productionとstrictでは、X投稿を表示または公開利用する前に必ず再取得する。
- strictモードは環境単位、ユーザー単位、記事単位、データソース単位、トピック単位で判定する。
- 環境単位のTTLはシステム全体の上限・安全装置として必ず優先する。
- ユーザー、記事、データソース、トピック単位の設定は、環境単位より緩くできず、厳しくする方向のみ許可する。
- news、trend、legal、price、medical、financeなど鮮度やリスクが高いトピックは短TTLまたはstrict扱いにできる。
- トピック自動判定は`normal`、`strict`、`compliance_strict`の3区分とする。
- `compliance_strict`はMVPでは`strict + 人間確認必須フラグ`として扱う。
- `normal`は一般SEO記事、ハウツー記事、技術メモ、ブログ下書き、evergreen記事を対象とする。
- `strict`は最新情報、ニュース、X投稿を使う記事、料金比較、API料金、商品価格、在庫、ランキング、口コミ・評判、SaaS比較を対象とする。
- `compliance_strict`は医療、法律、税金、投資、保険、政治、選挙、災害、事件、個人や企業の不祥事を対象とする。
- 初期辞書は`freshness`、`newsTrend`、`pricing`、`productAvailability`、`comparisonReview`、`techSaaS`、`legalFinanceHealth`、`politicsSafetyReputation`、`sourceSignals`のカテゴリで管理する。
- `legalFinanceHealth`と`politicsSafetyReputation`は`compliance_strict`として扱い、それ以外のstrict辞書カテゴリは`strict`として扱う。
- トピック判定辞書のメンテナンス担当は運営者本人とする。
- 辞書は月1回、または誤判定に気づいた時に更新する。
- 更新対象はstrict判定キーワード、除外キーワード、トピックカテゴリ、TTLポリシーとする。
- 辞書はYAMLまたはJSONで管理し、テストケースを追加し、判定結果を確認してから反映する。
- 検索結果の取得元URL、タイトル、スニペット、投稿ID、取得日時を保存する。

### 6.8 画像生成

- MVPでは画像生成を提供しない。
- MVPではアイキャッチ画像も作成しない。外部画像URLの保存・表示、画像ファイル保存、画像メタデータ保存も提供しない。
- 記事タイトルを画像に重ねる機能、画像再生成、画像生成履歴は後続フェーズで対応する。

### 6.9 WordPress連携

- ユーザーは複数のWordPressサイトを登録できる。
- WordPress REST APIとApplication Passwordを用いて投稿する。
- 投稿先カテゴリはWordPress REST APIから都度取得して選択できる。MVPではカテゴリ一覧をDBキャッシュしない。
- WordPressサイトごとに管理人プロフィール、語り手・キャラ設定、読者ペルソナを保存できる。
- 記事作成時にサイトを選択すると、そのサイト別ライティング設定をもとにAIが記事を生成する。
- 投稿時はタイトル、本文HTML、タグ、カテゴリを送信する。
- 投稿ステータスは下書きをデフォルトとし、ユーザーが明示した場合のみ公開投稿を許可する。
- `compliance_strict`または`HumanReviewRequired`の記事は、人間確認完了前に公開投稿できない。下書き投稿は可能とする。
- 投稿成功時は投稿URLと投稿IDを保存する。
- 投稿失敗時はエラー内容を保存し、再投稿できるようにする。

### 6.10 通知連携

- 記事作成完了、投稿完了、ジョブ失敗時に通知を送信できる。
- MVPではDiscord Webhook通知を想定する。
- Discord Webhook URLの保存、送信テストを提供する。
- 通知の送信結果をログとして保存する。

### 6.11 インポート・一括作成

- 改行区切りのキーワードを一括登録できる。
- `キーワード|タイトル` 形式でタイトル付き登録ができる。
- CSVインポートは後続フェーズで対応する。
- 一括作成はジョブキューに登録し、順次処理する。
- 一括作成では任意でWordPress自動投稿を指定できる。自動投稿は明示的に有効化した場合のみ行い、投稿ステータスは下書きを既定とする。

## 7. データ要件

主要エンティティは以下とする。

| エンティティ | 主な項目 |
| --- | --- |
| User | ID、メール、表示名、ロール、利用上限、利用量、状態 |
| Article | ID、UserId、Keyword、Title、Status、Tone、Tags、Memo、SuggestedKeywords、RelatedKeywords、LearningType、LearningText、AdditionalPrompt、Body、HtmlBody、MetaDescription、WritingProfileWordpressSiteId、WritingProfileSnapshotJson、HumanReviewRequired、HumanReviewedAt、AutoPostToWordpress、AutoPostWordpressSiteId、DeletedAt |
| ArticleHeading | ID、ArticleId、ParentId、Level、Title、Body、Order、TargetLength、Status |
| ArticleGenerationJob | ID、ArticleId、JobType、Status、Priority、PayloadJson、ErrorMessage、StartedAt、FinishedAt |
| AiGenerationLog | ID、UserId、ArticleId、Model、PromptChars、OutputChars、CostUnit、PromptHash、CreatedAt |
| SearchResult | ID、ArticleId、Query、Title、Url、Snippet、FetchedAt |
| XSearchPost | ID、ArticleId、Query、PostId、AuthorId、Text、Url、PostedAt、FetchedAt |
| WordpressSite | ID、UserId、SiteName、BaseUrl、LoginId、EncryptedApplicationPassword、DefaultCategoryId、SiteAdminProfile、WritingCharacter、ReaderPersona |
| WordpressPost | ID、ArticleId、WordpressSiteId、PostId、PostUrl、Status、ErrorMessage |
| NotificationSetting | ID、UserId、Provider、EncryptedWebhookUrl、DestinationMasked、Enabled |
| NotificationLog | ID、UserId、ArticleId、Provider、Status、Message、CreatedAt |

## 8. 外部連携要件

### 8.1 AIモデル

- MVPで採用するAIプロバイダーはGoogle Geminiとする。
- MVPで採用するAIモデルはGoogle Gemini 3.1 Pro Previewとする。
- テキスト生成モデルは将来複数選択できる設計にするが、MVPの既定モデルはGoogle Gemini 3.1 Pro Previewに固定する。
- Gemini以外のAIプロバイダー対応はMVP対象外とし、後続フェーズでOpenAI GPT、Anthropic Claudeなどを選択できるようにする。
- プロバイダー差分はサービス層で吸収し、後続フェーズで別Providerを追加できるようにする。
- 後続フェーズでProvider別TokenCounterを実装し、公式APIまたは公式Tokenizerを使ってトークン数を事前見積もりする。
- モデル名、最大入力文字数、最大出力文字数、利用可否を設定で管理する。
- APIキーはソースコード、Git、平文設定ファイルに保存しない。
- Gemini 3.1 Pro PreviewのAPIモデルIDは`gemini-3.1-pro-preview`とする。
- Gemini 3.1 Pro Previewの利用可能リージョンはJapanとする。

### 8.2 Web検索

- Web検索APIはTavilyを採用する。
- X投稿検索はX API Full-Archive Searchを採用する。
- 検索APIは差し替え可能なインターフェースにする。
- 検索結果の利用可否、取得件数、対象地域を設定できる。
- X投稿検索では検索期間、言語、除外演算子、最大件数、キャッシュ有効期間を設定できる。
- 同じX投稿は外部投稿IDで重複排除する。
- X APIの契約はPay-per-useとする。
- X API Full-Archive Searchは必要時のみ実行する。
- X API Full-Archive Searchの`max_results`は通常100、大量調査時500を上限とする。
- 月間安全上限は10,000から50,000 posts程度から開始し、運用状況を見て調整する。
- X Post IDは長期保持し、X投稿本文は6から24時間保持し、公開前に再取得する。

### 8.3 WordPress

- Application Passwordは暗号化して保存する。
- 通信はHTTPSのみ許可する。
- 投稿前に接続テストを実行できる。
- 一括作成からの自動投稿は、投稿先WordPressサイトの所有者確認後にジョブ化し、生成済みHTML本文がある記事のみ対象とする。
- WordPress投稿時のアイキャッチ画像指定はMVPでは提供しない。URL指定とWordPressメディアアップロードは後続フェーズで対応する。

### 8.4 通知

- 通知プロバイダーはDiscordを初期対象とする。
- 初期実装ではDiscord以外の通知プロバイダーを提供しない。
- 将来的にSlack、メールなどを追加できる設計にする。

## 9. バックグラウンド処理要件

- `BackgroundService`でDB上のジョブをポーリングまたはキュー購読して処理する。
- ジョブ処理では`IDbContextFactory`またはスコープ生成を用いてEF CoreのDbContextを扱う。
- ジョブはキャンセル、再試行、失敗記録に対応する。
- ジョブ種別は以下を想定する。
  - TitleGeneration
  - OutlineGeneration
  - BodyGeneration
  - Rewrite
  - WebSearch
  - XFullArchiveSearch
  - WordpressPost
  - Notification
- ジョブの多重実行を避けるため、ステータス更新はトランザクションで行う。
- MVPではWebアプリ内のHosted Serviceとして実装し、負荷が増えた場合はWorkerコンテナ分離を検討する。

## 10. 非機能要件

### 10.1 セキュリティ

- HTTPSを必須とする。
- 認証済みユーザー以外は管理画面にアクセスできない。
- ユーザー間のデータ分離を徹底する。
- CSRF対策を行う。
- 外部APIキーは安全なシークレット管理に保存する。
- WordPress Application PasswordとDiscord Webhook URLはユーザー別にDB暗号化保存する。
- ログにプロンプト全文、認証情報、APIキーを出力しない。

### 10.2 性能

- 記事一覧はページングを必須とする。
- 生成処理はHTTPリクエスト内で完了を待たず、ジョブ登録後にステータス表示する。
- 大きな本文、生成履歴、検索結果は必要な範囲だけ取得する。
- MVPでは画像を扱わないため、サムネイル最適化は後続フェーズで対応する。

### 10.3 可用性・運用

- ヘルスチェックを提供する。
  - Webアプリ稼働
  - PostgreSQL接続
  - BackgroundService稼働状態
- Docker Composeで以下のサービスを起動できる。
  - app
  - postgres
  - caddy
- PostgreSQLのデータはDocker volumeに永続化する。
- DBバックアップ手順を用意する。
- アプリログ、ジョブログ、エラーログを確認できる。

### 10.4 保守性

- UIコンポーネント、業務サービス、外部連携、データアクセスを分離する。
- AIプロンプトはコードに直書きせず、テンプレートまたは設定として管理する。
- 外部プロバイダーはインターフェース経由で差し替え可能にする。
- EF CoreのマイグレーションでDBスキーマを管理する。

## 11. Docker / VPS 構成要件

### 11.1 Docker Compose

想定サービス:

- `app`: ASP.NET Core / Blazor Web App
- `postgres`: PostgreSQL
- `caddy`: HTTPS終端、リバースプロキシ

環境変数として最低限以下を扱う。

- `ASPNETCORE_ENVIRONMENT`
- `ConnectionStrings__DefaultConnection`
- `Authentication__CookieName`
- `AiProviders__Gemini__ApiKey`
- `Security__DataProtectionKeysPath`

設定値の詳細は [設定リファレンス](configuration-reference.md) を参照する。本番用の秘密情報はGit管理しない。

### 11.2 Caddy

- 独自ドメインでHTTPS化する。
- CaddyからappコンテナのKestrelへリバースプロキシする。
- HTTPからHTTPSへリダイレクトする。
- 必要に応じてアップロードサイズやタイムアウトを調整する。

### 11.3 VPS

- Docker EngineとDocker Compose Pluginを利用する。
- ファイアウォールでは80/443/SSHのみ公開を基本とする。
- PostgreSQLポートは外部公開しない。
- 定期バックアップとログローテーションを設定する。

## 12. テスト要件

- ユニットテスト
  - 記事ステータス遷移
  - 文字数利用履歴記録
  - プロンプト生成
  - WordPress投稿リクエスト生成
- 結合テスト
  - 認証済み/未認証アクセス
  - 記事作成ジョブ登録
  - EF CoreとPostgreSQLの基本CRUD
  - WordPress連携の成功/失敗
- E2Eテスト
  - 記事作成から構成生成まで
  - 一括登録からジョブ作成まで
  - 記事一覧検索
  - WordPress投稿モーダル操作

## 13. MVPスコープ

MVPで実装する範囲:

- ログイン
- 記事一覧
- 単体記事作成
- 一括キーワード登録
- タイトル候補生成
- 見出し構成生成
- 見出し単位の本文生成
- 記事本文編集
- HTML変換
- WordPressサイト登録
- WordPress投稿
- Discord通知設定と送信テスト
- 利用文字数の記録・表示
- Docker ComposeによるVPSデプロイ

MVP外または後続フェーズ:

- キーワード発掘機能の完全実装
- リサーチ専用画面
- 画像生成
- アイキャッチ画像作成
- 外部画像URLの保存・表示
- 画像メタデータ保存
- 画像生成の高度な編集
- WordPressメディアアップロード
- WordPress投稿時のアイキャッチ画像URL指定
- note直接投稿
- note投稿用変換
- Discord以外の通知プロバイダー
- ライター管理
- 複数ワーカー構成
- 利用文字数の課金換算
- 課金決済
- 多言語対応
- Gemini以外のAIプロバイダー選択

## 14. 受け入れ基準

- ユーザーはログイン後、記事作成画面を開ける。
- ユーザーはキーワードを入力し、タイトル候補を生成できる。
- ユーザーはキーワードと設定から見出し構成を生成できる。
- ユーザーは見出しごとに本文を生成・編集・保存できる。
- ユーザーは記事一覧から生成済み記事を検索・表示できる。
- ユーザーはWordPress連携先を登録し、記事を投稿できる。
- WordPress投稿モーダルの投稿ステータス初期値は下書きである。
- AI生成、投稿、通知の失敗内容が画面またはログで確認できる。
- Docker Composeでapp、postgres、caddyが起動し、Caddy経由でHTTPSアクセスできる。
- PostgreSQLデータはコンテナ再起動後も保持される。
