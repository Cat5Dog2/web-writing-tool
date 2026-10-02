# 要確認リスト

確認日: 2026-10-02。状態: 対応方針は2026-10-02に決定し、[todo.md](../todo.md)の17.2へ登録済み。対象コード・既存記述は未修正。実装との食い違い14件、バグの疑い8件。

実装コードを現行動作の根拠として、ルートREADME、`docs/*.md`、`todo.md`と照合した記録である。依頼に従い、食い違う記述と対応するコードは変更していない。仕様上の意図、運用上の方針、バグの可能性を判断し、下記の「対応方針」で修正方法を決めた。修正は登録した各タスクで行う。

「実装との食い違い」は読み取れた実装と記述の差、「バグの疑い」は具体的な入力・状態・実行順序から静的に追跡した懸念である。将来の設計方針そのものをバグと断定しない。並行実行、本番Cookie、実APIでの再現は行っていない。

## 対応方針（2026-10-02決定）

各項目の「要確認」に対する方針と登録タスクである。タスクの完了時に、該当項目を対応済みとして記録する。

| 項目 | 対応方針 | タスク |
| --- | --- | --- |
| B-008 | 無効化時にSecurityStampを更新し、Cookie検証とBlazor circuitの再検証で有効状態を確認する。無効ユーザーのパスワード変更を拒否する。26.2の手順3に向けて、管理者による一時パスワード設定を追加する | T-1353、T-1354 |
| B-001・Q-013 | 投稿は記事の保存内容を正とし、投稿ダイアログは確認用の表示にする。Payloadには本文を保存せず、登録時の内容ハッシュで実行時に照合する | T-1355 |
| B-005 | トランザクション内で記事行をロックして重複を確認する。実行中のWordPress投稿ジョブには部分一意索引を付ける | T-1356 |
| B-009 | WordPress投稿は429だけ自動再試行し、結果が不明な失敗はWordPress側の確認後に再投稿する。送信前に送信中を記録し、失敗の記録とロック期限切れの復旧ではどちらも投稿履歴の状態で扱う。成功済みは`Succeeded`として結果を復元し（失敗通知は出さない）、送信済みで結果が分からなければ再送しない。未使用の`Wordpress:RetryCount`も削除する | T-1357 |
| B-003 | 構成再生成・リライト・本文の部分生成でも、見出しから本文を組み直してHtmlBodyを無効化する | T-1358 |
| B-006 | キャンセルを`Queued`条件付きの更新にし、成功・失敗の記録は`Running`からだけ遷移させる | T-1359 |
| B-004 | パーサーで型を確認し、一括生成以外のジョブの最終失敗時にも記事状態を戻す | T-1360 |
| Q-007 | 要件どおり登録時に残数を確認する。確認と登録はユーザー単位で直列化し、待機中で未開始の構成生成ジョブと、構成生成前の段階にある待機中・実行中の一括生成Runを予約数に含める（キャンセル・停止・失敗したものは含めない）。消費は構成生成ジョブの初回の実行開始時に行い、返却はしないため、管理者が0にした停止状態はキャンセルで解除されない | T-1361 |
| Q-005・Q-006 | 本文なしの404と英語の`detail`を直す。`errorCode`・`traceId`は外部公開API（`/api/v1`）の導入時に追加する | T-1362 |
| Q-001・Q-002・Q-004・Q-008 | 文書を実装に合わせる。ProblemDetailsは共通登録せず各Endpointで返すと記載し、`errorCode`・`traceId`の共通付与は`/api/v1`導入時とする。`/health/deps`は管理者限定のため、HTTPステータスは変えず本文で判定する | T-1363 |
| Q-003・Q-009〜Q-012 | 文書を実装に合わせ、拡張は後続フェーズ候補とする | T-1364 |
| Q-014・B-007 | 再投稿は新規投稿になること、1プロセス1Workerが前提であることを文書に明記する。差し替え機能とロック延長は後続フェーズ候補とする | T-1365 |

## 実装との食い違い

### Q-001 Optionsクラス名と設定セクション

- 文書: [設定リファレンス](configuration-reference.md)5には`AppOptions`、`AiProviderOptions`、`SearchCacheOptions`、`UsageLimitOptions`、`SeedOptions`がある。このうち[基本設計](basic-design.md)4.3には`AiProviderOptions`・`UsageLimitOptions`、[コーディング規約](coding-guidelines.md)4.3と[外部連携設計](external-integration-design.md)5.2には`AiProviderOptions`がある。
- 実装: [DI登録](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)では、Geminiに`GeminiOptions`（`AiProviders:Gemini`）、検索キャッシュに`SearchCachePolicyOptions`（`SearchCache`）を使用する。上記5クラスは実装されていない。設定リファレンスには`ExternalApis`の行も重複する。
- 要確認: 設計上の予定クラスを残すのか、現在のクラス・セクション対応に更新するのか。

### Q-002 設定しても実行動作に反映されないキー

[設定リファレンス](configuration-reference.md)5、6.1、6.6、6.8、6.9、6.11の次の記述を確認する。

| キー | 実装で確認した状態 | 確認事項 |
| --- | --- | --- |
| `BackgroundJobs:DefaultMaxAttempts` | [BackgroundJobOptions](../src/WebWritingTool.Infrastructure/BackgroundJobs/BackgroundJobOptions.cs)にプロパティがない。[JobRetryPolicy](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobRetryPolicy.cs)が種別ごとに2回または3回と決める | 設定可能にする意図があるか |
| `Wordpress:RetryCount` | [WordpressOptions](../src/WebWritingTool.Infrastructure/Wordpress/WordpressOptions.cs)に既定0のプロパティはあるが、Client・ジョブ処理から参照されない | Clientの再試行設定なのか、ジョブの再試行だけでよいのか |
| `Security:CookieSecurePolicy` | [Cookie設定](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)はDevelopmentで`SameAsRequest`、それ以外で`Always`を直接指定する | 設定による変更を認める意図があるか |
| `Seed:Enabled` | アプリ側にバインド・参照処理がない。[IdentityDataSeeder](../src/WebWritingTool.Infrastructure/Identity/IdentityDataSeeder.cs)は起動時に呼ばれ、テストデータはFixtureが投入する | テストSeedの切り替えとして残すか。関連する[テスト設計](test-design.md)16.1も対象 |
| `App:BaseUrl` | アプリ内に参照処理がない。[通知Handler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/NotificationJobHandler.cs)のWordPress投稿通知は投稿結果のURLを使う | 公開URLを必須設定としている用途はどこか |

### Q-003 サービス一覧の名前・配置

- 文書: [基本設計](basic-design.md)10.1、[ジョブ設計](job-design.md)5.1〜5.2、[コーディング規約](coding-guidelines.md)7に`IArticleService`、`IHeadingService`、`IGenerationJobService`、`IUsageLimitService`、`IReferenceResearchService`などを記載する。
- 実装: Application層の契約は`IArticleQueryService`、`IArticleCommandService`、`IArticleHeadingService`、`IJobQueryService`、`IJobCommandService`、`IUsageQueryService`、`IArticleResearchService`などであり、DBを使う実装はInfrastructure層にある。登録の根拠は[ServiceCollectionExtensions.cs](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)。
- 要確認: 一覧を概念上の設計名として残すのか、現在の契約・実装名・配置を示す一覧にするのか。

### Q-004 DbContextFactory・圧縮・ProblemDetails・OpenAPIの登録

- 文書: [基本設計](basic-design.md)4.1で`IDbContextFactory<ApplicationDbContext>`、Response Compression、ProblemDetailsの登録を挙げ、[API設計](api-design.md)17では開発・内部確認用OpenAPIを生成する。
- 実装: [Program.cs](../src/WebWritingTool.Web/Program.cs)、[DI登録](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)、[Webプロジェクト](../src/WebWritingTool.Web/WebWritingTool.Web.csproj)に`AddDbContextFactory`、Response Compressionの登録・適用、`AddProblemDetails`、OpenAPIの登録・ルートがない。DbContextは`AddDbContext`、エラーは各Endpointの`Results.Problem`等で扱う。
- 要確認: 現行構成へ記述を合わせるか、未実装の設計要件として明示するか。

### Q-005 APIのProblemDetails形式

- 文書: [エラーコード](error-codes.md)2・4は`errorCode`と`traceId`を必須とし、問題種別URLを定義する。一方、[API設計](api-design.md)4.4の例と5.3のDTOには`traceId`はあるが`errorCode`がなく、文書間でも契約が一致していない。
- 実装: [ArticleEndpoints.cs](../src/WebWritingTool.Web/Endpoints/ArticleEndpoints.cs)の`ToProblemResult`等は`Results.Problem` / `Results.ValidationProblem`へこれらの拡張値を渡さず、共通のカスタマイズ登録もない。`Results.NotFound()`など本文なしのエラーもあり、[Program.cs](../src/WebWritingTool.Web/Program.cs)のステータスコードページ再実行が適用される可能性がある。
- 要確認: 必須フィールドと業務ErrorCodeの契約を実装するのか、現行レスポンスを記述するのか。本文なし404のContent-Type・応答本文はHTTPでの確認が必要。

### Q-006 利用者向けエラー文言

- 文書: [エラーコード](error-codes.md)2・4は`detail`、`errors`、画面向け概要を「です・ます」調とする。
- 実装: [ArticleEndpoints.cs](../src/WebWritingTool.Web/Endpoints/ArticleEndpoints.cs)は競合時に`Running jobs exist for this article.`、`The article was updated by another request.`を返す。[レート制限](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)も`Too many requests.`を返す。
- 要確認: 日本語文言をAPIにも適用するか。英語の`title`は文書上も対象外なので、この差異には含めない。

### Q-007 構成生成残数のチェック・消費

- 文書: [API設計](api-design.md)16.3・19、[ジョブ設計](job-design.md)7.2・5.2は、`remainingOutlineCount`不足時にジョブ登録を拒否する。API設計15.5の管理者向け手順にも、新規構成生成を止める場合は`remainingOutlineCount = 0`にするとある。
- 実装: `RemainingOutlineCount`は[管理者サービス](../src/WebWritingTool.Infrastructure/Admin/AdminUserService.cs)で保存し、[利用情報サービス](../src/WebWritingTool.Infrastructure/Usage/UsageQueryService.cs)で表示する。[JobService.EnqueueAsync](../src/WebWritingTool.Infrastructure/Jobs/JobService.cs)、[ArticleService.BulkCreateAsync](../src/WebWritingTool.Infrastructure/Articles/ArticleService.cs)、構成生成Handlerには残数確認・減算がない。
- 要確認: 残数0でも生成できるため、記載された管理者向け停止手順は現状では効果がない。現行動作を採用するか、残数制御が必要か。月次文字数集計を行わない設計とは分けて判断する。

### Q-008 `/health/deps`の意味

- 文書: [観測性・ログ](observability-logging.md)15はGemini・Tavily・X・WordPress・Discordの簡易疎通を確認するとする。同様の記述は[基本設計](basic-design.md)16.2、[環境構築](environment-setup.md)12、[運用](operation-design.md)7.2、[セキュリティ](security-design.md)20.1、[テスト設計](test-design.md)14.3にもある。
- 実装: [ExternalDependencyConfigurationHealthCheck.cs](../src/WebWritingTool.Web/HealthChecks/ExternalDependencyConfigurationHealthCheck.cs)はGemini・Tavily・Xの認証設定が空でないかを確認するだけで、外部通信しない。WordPress・Discordは対象外。ダミーモードではHealthyとなる。設定不足時はDegradedとなるが、[Program.cs](../src/WebWritingTool.Web/Program.cs)は`HealthCheckOptions.ResultStatusCodes`を変更しておらず、ASP.NET Coreの既定値ではDegradedもHTTP 200を返す。
- 要確認: 設定確認用のEndpointとして記述するか、疎通チェックを別途実装するか。現状のHealthyは接続成功の証拠にならず、HTTPステータスだけを見る監視は設定漏れも検知できない。実HTTPでの再現は未実施。

### Q-009 構造化ログのイベント契約

- 文書: [観測性・ログ](observability-logging.md)6・9〜12は`eventName`を必須とし、`ApiRequestCompleted`、`ProblemDetailsReturned`、`JobStarted`等の安定したイベント名と共通項目を定義する。
- 実装: [ArticleJobWorker.cs](../src/WebWritingTool.Web/BackgroundJobs/ArticleJobWorker.cs)は`Job started.`等のメッセージと`jobId`・`jobType`を出すが、`eventName`フィールドを渡していない。[Program.cs](../src/WebWritingTool.Web/Program.cs)にもAPI完了・ProblemDetails返却イベントの共通記録処理はない。
- 要確認: ログを定義したイベント契約へ統一するか、現行テンプレートと記録項目を運用資料に示すか。

### Q-010 ユーザー・記事・データソース単位のTTL設定

- 文書: [データ保持](data-retention-privacy.md)6、[セキュリティ](security-design.md)13.2、[テスト設計](test-design.md)17は、ユーザー・記事・データソース単位でTTLを厳しく指定できるとする。
- 実装: [SearchCachePolicyResolver](../src/WebWritingTool.Application/Search/SearchCachePolicy.cs)には短縮TTLを受け取る任意引数があるが、本番コードの呼び出し元である[WebSearchJobHandler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/WebSearchJobHandler.cs)、[XFullArchiveSearchJobHandler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/XFullArchiveSearchJobHandler.cs)、[XPostRehydrationService](../src/WebWritingTool.Infrastructure/Search/XPostRehydrationService.cs)はいずれも取得日時・トピック区分だけを渡す。実運用では環境ポリシーとトピック区分で解決し、ユーザー別・記事別のTTL設定保存先やそれらを読み込む処理はない。
- 要確認: 呼び出し側から渡せるTTLだけを指す設計なのか、利用者が永続設定できる要件なのか。

### Q-011 生成JSONのコードフェンス

- 文書: [プロンプト設計](prompt-design.md)15は、JSON出力の余計な説明やコードフェンスを拒否する。
- 実装: [GenerationOutputParsers.cs](../src/WebWritingTool.Application/Generation/GenerationOutputParsers.cs)の`StripCodeFence`は、先頭がコードフェンスの場合に先頭・末尾の行を取り除いてJSONを解釈する。
- 要確認: フェンス付きJSONを許容して取り込む現行方針にするか、出力形式を厳格に拒否するか。

### Q-012 AIへ渡す参考情報のデータ形

- 文書: [プロンプト設計](prompt-design.md)13は`sourceId`、`sourceType`、`publishedAt`、`retrievedAt`、`excerpt`、`reliabilityNote`等を挙げる。
- 実装: [AiReferenceSource](../src/WebWritingTool.Application/Generation/AiGenerationContracts.cs)は`SourceId`、`Title`、`Url`、`Summary`の4項目で、[ArticleResearchService.cs](../src/WebWritingTool.Infrastructure/Search/ArticleResearchService.cs)は取得日時と抜粋をSummaryの文字列へ入れる。[ReferencePromptFormatter.cs](../src/WebWritingTool.Application/Generation/ReferencePromptFormatter.cs)はこのレコードをJSON化する。
- 要確認: 現行4項目を記述するか、資料種別・日時・信頼性を独立した項目へ拡張するか。

### Q-013 WordPress投稿PayloadにHTML全文を保存

- 文書: [データ保持](data-retention-privacy.md)9・13、[プロンプト設計](prompt-design.md)16は、`ArticleGenerationJobs.PayloadJson`へ記事本文全文を保存しないとする。一方、[ジョブ設計](job-design.md)8.5のWordPress Payloadには`htmlBody`がある。
- 実装: [WordpressPostPayload](../src/WebWritingTool.Application/Wordpress/WordpressContracts.cs)と[WordpressPostService.cs](../src/WebWritingTool.Infrastructure/Wordpress/WordpressPostService.cs)は、投稿HTML全文をPayloadJsonへ保存する。ジョブ履歴は自動削除しないため、記事の現在値とは別にHTMLが残る。
- 要確認: 投稿内容の保存を許可する例外と保持期間を定義するか、Payloadへ本文を保存しない方針を維持するか。

### Q-014 WordPress再投稿と既存記事の差し替え

- 文書: [コンテンツ更新・メンテナンス](content-update-maintenance.md)12は、投稿ID・URLを確認したうえで下書き更新または既存記事の差し替えを行う手順を示す。
- 実装: [WordpressClient.CreatePostAsync](../src/WebWritingTool.Infrastructure/Wordpress/WordpressClient.cs)は常に`POST wp-json/wp/v2/posts`を呼ぶ。[投稿Handler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/WordpressPostJobHandler.cs)も既存PostIdを更新先として渡さない。アプリから再度投稿すると新規投稿になる。
- 要確認: この手順はWordPress側の手動操作なのか、アプリの差し替え機能を要求するのか。アプリからの再投稿を既存記事更新として案内できる状態ではない。

## バグの疑い

### B-001 確認済み記事と異なる投稿内容をPublishできる可能性

- 条件: `HumanReviewRequired=true`で人間確認済みの記事に、保存値と異なるタイトル・HTMLを投稿リクエストで指定する。
- 根拠: [WordpressPostService.CreatePostJobAsync](../src/WebWritingTool.Infrastructure/Wordpress/WordpressPostService.cs)は記事の`HumanReviewedAt`で公開可否を判定し、その後にリクエストのタイトル・HTMLをPayloadへ入れる。確認済み内容との比較・確認状態の解除がなく、[投稿Handler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/WordpressPostJobHandler.cs)も記事の確認状態で判定する。
- 影響・要確認: [記事品質](article-quality-guidelines.md)14、[コンテンツ表示](content-rendering-design.md)13の「確認後に公開判断へ影響する値を変更した場合は未確認へ戻す」が投稿フォームの変更にも必要か。確認対象と実際の送信内容を結び付ける必要性を判断する。実投稿は未実施。

### B-003 構成再生成・リライト・本文生成後に旧本文・HTMLが残る可能性

- 条件: 本文・プレビューHTMLが保存された記事で、構成を再生成する、見出し本文をリライトする、または全見出しがそろわない状態で本文生成を完了する。
- 根拠: [OutlineGenerationJobHandler.HandleAsync](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/OutlineGenerationJobHandler.cs)は旧見出しを論理削除して新見出しを追加するが、`article.Body`と`article.HtmlBody`を更新・無効化しない。[RewriteJobHandler.HandleAsync](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/RewriteJobHandler.cs)も`article.Body`を更新する一方でHtmlBodyを無効化しない。[BodyGenerationJobHandler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/BodyGenerationJobHandler.cs)は全見出しが生成済みのときだけHtmlBodyを再変換し、そろわない場合は`article.Body`だけを組み直す。[ArticlePreview.razor](../src/WebWritingTool.Web/Components/Pages/ArticlePreview.razor)は保存済みHtmlBodyを表示し、WordPress投稿ダイアログも保存済みHtmlBodyを初期値に使う。手動の見出し編集では[ArticleHeadingService.UpdateArticleBody](../src/WebWritingTool.Infrastructure/Articles/ArticleHeadingService.cs)がHtmlBodyをnullにする。
- 影響・要確認: 構成再生成では新構成と旧本文が組み合わされ、各経路で古いHTMLがプレビュー・投稿される可能性がある。[画面設計](screen-design.md)12.2のプレビュー表示方針に沿って、これらの経路でもHTMLを無効化するか。画面での再現は未実施。

### B-004 不正な生成JSONが例外処理をすり抜け、記事状態が残る可能性

- 条件: `TitleCandidateParser.Parse`へ`["候補A"]`、`OutlineGenerationParser.Parse`へ`[{"title":"見出しA"}]`または`{"headings":["見出しA"]}`を渡す。
- 根拠: [GenerationOutputParsers.cs](../src/WebWritingTool.Application/Generation/GenerationOutputParsers.cs)はトップレベルを代替の配列として使う分岐の前に`root.TryGetProperty(...)`を呼び、見出し要素にも型確認なしで同メソッドを使う。非オブジェクトに対するこの呼び出しは`InvalidOperationException`となり、[OutlineGenerationJobHandler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/OutlineGenerationJobHandler.cs)の`catch (JsonException)`を通らない。[ArticleJobWorker](../src/WebWritingTool.Web/BackgroundJobs/ArticleJobWorker.cs)から[JobLeaseService](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobLeaseService.cs)へ渡され、`JobFailure.FromException`は`UnknownError`に分類する。[JobRetryPolicy](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobRetryPolicy.cs)では再試行対象である。
- 影響・要確認: 通常の構成生成では記事を`OutlineGenerating`にした後の失敗状態更新・失敗時`AiGenerationLog`の記録をすり抜け、再試行でGeminiを再度呼ぶ。試行上限でジョブがFailedとなっても、一括生成用の`GenerationRunId`がない場合は記事をFailedへ変える補完処理がなく、`OutlineGenerating`が残る経路がある。配列形式の許容範囲と形式不正のエラー変換・状態復旧を確認する。現在の要求形式はオブジェクトであり、通常の応答が必ず失敗するという意味ではない。実行再現・実API通信は未実施。

### B-005 通常ジョブを並行登録したときの重複

- 条件: 同じ記事のWordPress投稿、タイトル生成または検索を、2リクエストでほぼ同時に登録する。
- 根拠: [WordpressPostService.CreatePostJobAsync](../src/WebWritingTool.Infrastructure/Wordpress/WordpressPostService.cs)は実行中投稿ジョブをトランザクション開始前に確認し、`EnqueuePostJobAsync`では再確認せずジョブ・投稿履歴を追加する。手動投稿登録では記事も更新しない。[JobService.EnqueueAsync](../src/WebWritingTool.Infrastructure/Jobs/JobService.cs)も重複検索をトランザクション開始前に行い、通常のタイトル・検索登録は記事のRowVersionを変更しない。[ApplicationDbContext](../src/WebWritingTool.Infrastructure/Data/ApplicationDbContext.cs)の`ArticleId + JobType`索引は非一意で、一意制約は一括生成の`GenerationRunId + JobType`用である。
- 影響・要確認: 両方が「重複なし」を読んで登録を確定できる可能性がある。WordPress投稿は[ジョブ設計](job-design.md)7.3で明示された多重実行防止の対象であり、Q-014の新規投稿経路と重なるとWordPress上に記事が2件作られ得る。タイトル生成・検索は同表の明示対象ではないため、必要な重複抑止範囲も確認する。並行実行テスト・実投稿は未実施。

### B-006 通常QueuedジョブのキャンセルとWorker取得の競合

- 条件: 通常ジョブのキャンセルがQueuedを読んだ直後に、Workerが同じジョブをRunningへ変更する。
- 根拠: [JobService.CancelCoreAsync](../src/WebWritingTool.Infrastructure/Jobs/JobService.cs)は読込・状態確認・Canceled更新を行うが、取得と共通の行ロックや条件付き更新を使わない。ジョブEntityにRowVersionもない。[JobLeaseService](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobLeaseService.cs)の`MarkSucceededAsync`はSucceeded以外の状態を成功へ変更できる。`MarkFailureOrRetryAsync`も通常の失敗経路ではSucceededだけを除外し、Canceledを再試行のQueuedまたは終端のFailedへ変更できる。ただし、`ExternalApiDeferredException`の経路はRunning限定のガードがある。一括生成の停止は別のロック付き経路である。
- 影響・要確認: キャンセル成功後にも外部処理が走り、CanceledがSucceededで上書きされる、または失敗処理でQueuedへ戻って再実行される可能性がある。[ジョブ設計](job-design.md)13.1のQueued限定キャンセルを、状態遷移の原子性まで保証するか。競合の再現は未実施。

### B-007 長時間ジョブのロック更新

- 条件: 設定したLockTimeoutを超える処理の間に、別のWorkerが期限切れ復旧を実行する。
- 根拠: [ジョブ設計](job-design.md)9.3は処理中のLockedAt定期更新を求めるが、[JobLeaseService](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobLeaseService.cs)は取得時にのみLockedAtを設定し、[ArticleJobWorker](../src/WebWritingTool.Web/BackgroundJobs/ArticleJobWorker.cs)のheartbeatはメモリ上のWorker状態へ記録する。HandlerからDBロックを延長する処理はない。
- 影響・要確認: 実行中ジョブが期限切れとして再取得される可能性がある。現行の1プロセス1Workerでは通常の同時復旧は起きにくいが、複数プロセス・再起動・短いLockTimeoutでの扱いを確認する。複数Workerでの再現は未実施。

### B-008 無効化後の既存ログインセッション

- 条件: ログイン中のユーザーを管理者が`IsEnabled=false`に変更する。
- 文書: [エラーコード](error-codes.md)7は無効ユーザーを403の対象とし、[セキュリティ](security-design.md)26.2は不正ログイン疑いへの対応としてユーザーの一時無効化とセッション失効を別々の手順に挙げる。既存セッションも止める期待が文書にある。
- 根拠: [AdminUserService.UpdateUserAsync](../src/WebWritingTool.Infrastructure/Admin/AdminUserService.cs)はIsEnabledを保存するが、SecurityStamp更新やCookie破棄を行わない。[AccountEndpoints](../src/WebWritingTool.Web/Endpoints/AccountEndpoints.cs)はログイン時にIsEnabledを確認する一方、[Cookie設定](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)に有効状態を再確認する独自検証はない。所有者認可のActorにも有効状態は含まれない。
- 運用上の制約: [AdminEndpoints](../src/WebWritingTool.Web/Endpoints/AdminEndpoints.cs)に他ユーザーのセッションを失効させる操作はなく、本人のログアウトとは別に管理者がSecurityStampを更新する処理もない。仮にDBのSecurityStampを直接更新した場合、既存Cookieは発行から`SecurityStampValidatorOptions.ValidationInterval`（既定30分、アプリでの変更なし）を超えた最初のHTTPリクエストで検証され、拒否される。[Cookie設定](../src/WebWritingTool.Web/Configuration/ServiceCollectionExtensions.cs)（`ExpireTimeSpan`8時間、`SlidingExpiration`）では、SecurityStamp検証の成功時（Identityが`ShouldRenew`を設定）と、発行から4時間後以降の期限延長でCookieが更新される。検証間隔の30分は4時間より短いため、どちらの更新も検証を通過したリクエストでだけ起こる。DB更新後の検証は失敗するので、既存Cookieの発行時刻は更新前のままとなる。したがって即時失効ではなく、下記の再発行がない限り、更新後も最大30分は既存CookieでHTTPリクエストが通り得る。
- 失効の抜け道: 本人のパスワード変更（[AccountPasswordService](../src/WebWritingTool.Infrastructure/Accounts/AccountPasswordService.cs)）は`IsEnabled`を確認しない。成功後は[AccountEndpoints](../src/WebWritingTool.Web/Endpoints/AccountEndpoints.cs)の`RefreshSignInAsync`が、新しいSecurityStampを含むCookieを再発行する。DB更新後の最大30分の間にこの操作を行うと、以後の検証も通る。[セキュリティ](security-design.md)26.2が想定する不正ログインでは攻撃者が現在のパスワードを知っているため、現実的な経路である。また、開いているBlazor circuitの認証状態を更新する定期再検証Providerや明示的な切断処理も登録されていない。Cookie検証だけでは操作停止を保証できない。
- 手順の実行手段: 26.2の手順3「パスワードリセットを実施する」も、管理API・画面にリセット機能がなく（作成時の初期パスワードのみ）、`ResetPasswordAsync`等のリセット処理もコードにない。手順2のセッション失効と同じく、現状のアプリでは実行できない。
- 影響・要確認: 文書の無効ユーザー拒否・セッション失効の期待に対し、無効化操作だけでは既存Cookie、パスワード変更による再発行、Blazor circuitで操作を継続できる可能性がある。無効化に連動した失効（再発行の拒否、circuitの再検証を含む）を実装するか、26.2の手順2・3を実行する管理機能または運用手段を用意するかを確認する。実Cookie・circuitでの再現は未実施。

### B-009 WordPress投稿の自動再試行による二重投稿

- 条件: WordPress側で投稿が作成された後に、アプリ側がタイムアウト、通信エラー、5xx、応答不正などで失敗と判定する。または、投稿後にプロセスが停止し、ジョブが`Running`のまま残る。
- 文書: [ジョブ設計](job-design.md)9.3はロック期限切れのジョブを再試行可能なら`Queued`へ戻し、12.1はタイムアウト・500系・ネットワーク一時障害を再試行し、12.2は`WordpressPost`の最大試行回数を3回とする。コードは文書どおりであり、食い違いではなく設計上のリスクとして記録する。
- 根拠: [WordpressClient.CreatePostAsync](../src/WebWritingTool.Infrastructure/Wordpress/WordpressClient.cs)は、タイムアウトを`Timeout`、通信エラーを`NetworkError`、5xxを`ExternalServerError`、応答の解析失敗や`id`欠落を`ExternalBadResponse`とする。[投稿Handler](../src/WebWritingTool.Infrastructure/BackgroundJobs/Handlers/WordpressPostJobHandler.cs)はこれらをジョブのエラーコードへ変換し、[JobRetryPolicy](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobRetryPolicy.cs)ではいずれも再試行対象である。再試行時も既存PostIdを使わず、新規投稿として`POST`する（Q-014）。また、[JobLeaseService.RecoverExpiredLocksAsync](../src/WebWritingTool.Infrastructure/BackgroundJobs/JobLeaseService.cs)はロック期限が切れた`Running`ジョブを`Timeout`として再試行対象にし、再キューする。再実行時の投稿Handlerは、同じジョブの投稿履歴が成功済みでも`Queued`へ戻して投稿し直す。`Posted`の記事も投稿可能な状態のため、結果保存後・ジョブ完了記録前に停止した場合も再投稿となり、最初の`PostId`・`PostUrl`は上書きされる。
- 影響・要確認: 最初の要求がWordPress側で処理済みだった場合、再試行や復旧後の再実行で、WordPress上に同じ記事が最大3件作られ得る。結果が不明な失敗とロック期限切れの復旧で再送するかを確認する。実投稿での再現は未実施。

## レビュー後の訂正

2026-10-02の再照合で、B-002「参考情報をプロンプトへ二重に追加」は誤検知として撤回した。[ReferencePromptFormatter.Attach](../src/WebWritingTool.Application/Generation/ReferencePromptFormatter.cs)は元のSystemInstruction・UserPromptを保持し、整形後のハッシュ・文字数とReferencesだけを設定する。[GeminiTextGenerationClient](../src/WebWritingTool.Infrastructure/Generation/GeminiTextGenerationClient.cs)が送信時に一度だけ整形するため、二重追加はない。[ArticleResearchApiTests](../tests/WebWritingTool.IntegrationTests/Api/ArticleResearchApiTests.cs)にもハッシュ・文字数の一致を確認する既存テストがある（今回未実行）。他項目のIDは維持し、B-002は欠番とする。

## 照合範囲と検証の限界

- 主な根拠はEndpoint・DTO、DI/Options、Entity/EFマッピング、Identity、生成・検索・投稿・通知Handler、ジョブ取得・状態更新、画面のサービス呼出し、テストFixture、テストスクリプト、CI定義である。
- 実装と矛盾しない補足は、設定の`.env`読込、未記載の設定キー、一括生成のDTO、DBテーブル一覧・RowVersion、テストの実行範囲・Fixtureに追加した。
- 要件・品質基準・運用方針には手動で満たす内容や後続構想も含まれる。専用コードがないことだけで全項目を未実装・不具合と判定していない。
- 文書の差分は`git diff --check`で確認した。未追跡の本ファイルは同コマンドの対象外のため、末尾空白・UTF-8・改行・最終改行を別途確認した。Markdownの相対リンクも参照先の存在を確認した。
- 今回は文書の変更のみであり、アプリのビルド、単体・結合・E2E・性能テスト、DB/Migration適用、本番操作、外部API通信は行っていない。既存のテスト結果は今回の検証結果として流用しない。
