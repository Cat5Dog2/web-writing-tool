# API設計書

## 1. 目的

本書は、AIライティングツールのHTTP API仕様を定義する。対象は、Blazor Web Appから利用する内部API、および将来的な外部連携に備えたMinimal APIである。

APIはASP.NET Core Minimal APIで実装する。画面内で完結する単純な表示処理はApplicationサービスを直接呼び出してよいが、ジョブ登録、非同期状態取得、外部連携、将来公開の可能性がある処理はAPIとして境界を明確にする。
MVPでは外部公開APIを正式提供せず、`/api`配下はBlazor Web Appが同一オリジンで利用する内部APIとする。

## 2. 基本方針

- APIのベースパスは`/api`とする。
- JSONのプロパティ名はcamelCaseとする。
- 日時はUTCのISO 8601文字列で返す。
- IDはUUID文字列を使う。ただしIdentityユーザーIDはASP.NET Core Identityの既定に合わせてstringとする。
- 認証はCookie認証を基本とする。
- API境界で認可を必ず行い、UIの表示制御だけに依存しない。
- リクエストDTO、レスポンスDTO、DBエンティティは分離する。
- ジョブ登録系APIは同期実行せず、`202 Accepted`で`jobId`を返す。
- エラーはProblemDetails形式を基本とする。
- MVPの`/api`配下は外部互換性保証の対象外とする。
- 外部公開が必要になった段階で`/api/v1`を新設し、認証方式、スコープ、レート制限、監査ログ、互換性ポリシーを定義する。
- 管理者APIは外部公開対象に含めない。

## 3. ルートグループ

| グループ | ベースパス | 用途 |
| --- | --- | --- |
| Articles | `/api/articles` | 記事CRUD、検索、一括作成 |
| Headings | `/api/articles/{articleId}/headings` | 見出し操作 |
| Generation | `/api/articles/{articleId}/generation` | AI生成ジョブ登録 |
| Jobs | `/api/jobs` | ジョブ状態取得、再実行、キャンセル |
| WordpressSites | `/api/wordpress-sites` | WordPress連携先管理 |
| WordpressPosts | `/api/articles/{articleId}/wordpress-posts` | WordPress投稿 |
| Notifications | `/api/notifications` | 通知設定、送信テスト |
| Usage | `/api/usage` | 利用文字数、上限情報 |
| Account | `/api/account` | ログインユーザー本人のアカウント操作 |
| Admin | `/api/admin` | 管理者向け設定 |

## 4. 共通仕様

### 4.1 認証・認可

| 種別 | 方針 |
| --- | --- |
| 匿名API | ログイン、ヘルスチェックのみ |
| 認証必須API | `RequireAuthorization()`を適用 |
| 所有者チェック | `articleId`、`wordpressSiteId`、`jobId`から所有者を検証 |
| 管理者API | `RequireAuthorization("AdminOnly")`を適用 |

所有者または管理者が操作できるAPIでは、Applicationサービス内でも所有者検証を行う。APIルートだけで権限を完結させない。

### 4.2 HTTPステータス

| ステータス | 用途 |
| --- | --- |
| `200 OK` | 取得、更新、同期的な操作成功 |
| `201 Created` | リソース作成成功 |
| `202 Accepted` | ジョブ登録成功 |
| `204 No Content` | 削除、本文なし成功 |
| `400 Bad Request` | 入力不正 |
| `401 Unauthorized` | 未認証 |
| `403 Forbidden` | 権限不足 |
| `404 Not Found` | 対象なし、または権限上存在を隠す場合 |
| `409 Conflict` | 状態不整合、同時更新競合 |
| `422 Unprocessable Entity` | 形式は正しいが業務ルール違反 |
| `429 Too Many Requests` | レート制限 |
| `500 Internal Server Error` | 予期しないサーバーエラー |

### 4.3 ProblemDetails

エラー応答は以下を基本形とする。

```json
{
  "type": "https://example.com/problems/validation-error",
  "title": "Validation error",
  "status": 400,
  "detail": "入力内容を確認してください。",
  "instance": "/api/articles",
  "traceId": "00-...",
  "errors": {
    "keyword": [
      "キーワードは必須です。"
    ]
  }
}
```

### 4.4 ページング

一覧APIは以下のクエリを共通で受け付ける。

| Query | 型 | 既定値 | 制約 |
| --- | --- | --- | --- |
| `page` | int | 1 | 1以上 |
| `pageSize` | int | 10 | 1から100 |
| `sort` | string | APIごとに定義 | 許可された値のみ |
| `direction` | string | `desc` | `asc` / `desc` |

共通レスポンス:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1531,
  "totalPages": 154,
  "hasPrevious": false,
  "hasNext": true
}
```

### 4.5 ジョブ登録レスポンス

ジョブ登録系APIは以下を返す。

```json
{
  "jobId": "2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a",
  "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "jobType": "OutlineGeneration",
  "status": "Queued",
  "statusUrl": "/api/jobs/2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a"
}
```

## 5. 共通DTO

### 5.1 `PagedResult<T>`

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `items` | `T[]` | 取得結果 |
| `page` | int | 現在ページ |
| `pageSize` | int | 1ページ件数 |
| `totalCount` | int | 総件数 |
| `totalPages` | int | 総ページ数 |
| `hasPrevious` | bool | 前ページ有無 |
| `hasNext` | bool | 次ページ有無 |

### 5.2 `JobAcceptedResponse`

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `jobId` | uuid | 登録されたジョブID |
| `articleId` | uuid? | 関連記事ID |
| `headingId` | uuid? | 関連見出しID |
| `jobType` | string | ジョブ種別 |
| `status` | string | 初期ステータス |
| `statusUrl` | string | ジョブ状態取得URL |

### 5.3 `ValidationErrorResponse`

ProblemDetailsに`errors`を追加する。

| プロパティ | 型 | 説明 |
| --- | --- | --- |
| `type` | string | 問題種別URL |
| `title` | string | エラー名 |
| `status` | int | HTTPステータス |
| `detail` | string | 説明 |
| `instance` | string | APIパス |
| `traceId` | string | トレースID |
| `errors` | object | フィールド別エラー |

## 6. Articles API

### 6.1 記事一覧取得

```http
GET /api/articles
```

認可: 認証必須。一般ユーザーは自分の記事のみ、管理者は全記事を取得可能。

Query:

| Query | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `page` | int | 任意 | ページ番号 |
| `pageSize` | int | 任意 | 1ページ件数 |
| `q` | string | 任意 | タイトル、キーワード、メモ検索 |
| `tags` | string | 任意 | カンマ区切りタグ |
| `status` | string | 任意 | 記事ステータス |
| `createdFrom` | date | 任意 | 作成日From |
| `createdTo` | date | 任意 | 作成日To |
| `sort` | string | 任意 | `createdAt` / `title` / `status` |
| `direction` | string | 任意 | `asc` / `desc` |

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
      "createdAt": "2026-01-03T04:00:00Z",
      "headlineSource": "WebSearch",
      "status": "Completed",
      "statusLabel": "完了(16)",
      "title": "クラヲアクトミュージカルの魅力を徹底解剖！その秘密とは？",
      "keyword": "クラヲアクト,ミュージカル",
      "tags": ["クラヲアクト", "ミュージカル"],
      "memo": "",
      "generationModel": "gemini-3.1-pro-preview",
      "canPostToWordpress": true
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1531,
  "totalPages": 154,
  "hasPrevious": false,
  "hasNext": true
}
```

### 6.2 記事作成

```http
POST /api/articles
```

認可: 認証必須。

Request `CreateArticleRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `keyword` | string | 必須 | 1から200文字 |
| `title` | string | 任意 | 250文字以内 |
| `generateImage` | bool | 任意 | MVPではfalse固定。trueは`400 Bad Request` |
| `h2Count` | int? | 任意 | 1から20 |
| `h3Count` | int? | 任意 | 0から60 |
| `tone` | string | 任意 | `Normal`など |
| `tags` | string[] | 任意 | 各50文字以内 |
| `memo` | string | 任意 | 1000文字以内 |
| `suggestedKeywords` | string | 任意 | 10000文字以内 |
| `relatedKeywords` | string | 任意 | 10000文字以内 |
| `learningType` | string | 任意 | `None` / `Text` / `Url` |
| `learningText` | string | 任意 | 設定上限内 |
| `additionalPrompt` | string | 任意 | 3000文字以内 |
| `writingProfileWordpressSiteId` | uuid? | 任意 | サイト別ライティング設定を使うWordPressサイトID |
| `outlineMethod` | string | 必須 | `Keyword` / `Search` / `Ai` |
| `generationModel` | string | 必須 | 設定済みモデル |
| `searchMode` | bool | 必須 | 既定false |
| `notificationMode` | string | 任意 | `None` / `Discord` |

サイト別ライティング設定:

- `writingProfileWordpressSiteId`を指定した場合、対象サイトの`siteAdminProfile`、`writingCharacter`、`readerPersona`を記事作成時にスナップショットし、タイトル候補、見出し構成、本文生成、リライトのプロンプトへ反映する。
- 指定サイトはログインユーザーが所有する有効なWordPressサイトでなければならない。
- 未指定の場合はサイト別ライティング設定を使わない。

画像生成:

- MVPでは画像生成を行わないため、`generateImage = true`は受け付けない。
- 画像URL、画像生成条件、画像メタデータは保存しない。

Request例:

```json
{
  "keyword": "クラヲアクト,ミュージカル",
  "title": "クラヲアクトミュージカルの魅力を徹底解剖！その秘密とは？",
  "generateImage": false,
  "h2Count": 5,
  "h3Count": 12,
  "tone": "Normal",
  "tags": ["クラヲアクト", "ミュージカル"],
  "memo": "",
  "learningType": "Text",
  "learningText": "",
  "additionalPrompt": "",
  "writingProfileWordpressSiteId": "fb2a11db-849e-475d-8e79-9208e8f6f5af",
  "outlineMethod": "Search",
  "generationModel": "gemini-3.1-pro-preview",
  "searchMode": true,
  "notificationMode": "Discord"
}
```

Response `201 Created`:

```json
{
  "id": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "status": "Draft",
  "detailUrl": "/api/articles/7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c"
}
```

### 6.3 一括記事作成

```http
POST /api/articles/bulk
```

認可: 認証必須。

Request `BulkCreateArticlesRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `lines` | string[] | 必須 | `キーワード`または`キーワード|タイトル` |
| `h2Count` | int? | 任意 | 一括設定 |
| `h3Count` | int? | 任意 | 一括設定 |
| `isDomesticOnly` | bool | 必須 | 日本国内情報に限定 |
| `titleMethod` | string | 必須 | `Ai`など |
| `outlineMethod` | string | 必須 | 見出し構築方法 |
| `generationModel` | string | 必須 | 作成モデル |
| `searchMode` | bool | 必須 | 検索モード |
| `writingProfileWordpressSiteId` | uuid? | 任意 | サイト別ライティング設定を使うWordPressサイトID |
| `autoPostToWordpress` | bool | 任意 | 生成完了後にWordPress下書き投稿ジョブを自動登録する。既定false |
| `autoPostWordpressSiteId` | uuid? | 条件付き | `autoPostToWordpress`がtrueの場合は必須。投稿先WordPressサイト |
| `autoPostWordpressCategoryId` | int? | 任意 | 投稿カテゴリ。未指定時は投稿先サイトの既定カテゴリ |

自動投稿の扱い:

- `autoPostToWordpress`がtrueの場合、`autoPostWordpressSiteId`はログインユーザーが所有する有効なWordPressサイトでなければならない。
- `writingProfileWordpressSiteId`が未指定で`autoPostToWordpress`がtrueの場合、`autoPostWordpressSiteId`のサイト別ライティング設定を使用する。
- 一括作成APIのレスポンス時点ではWordPress投稿ジョブはまだ作成しない。本文生成とHTML変換が完了し、記事が投稿可能状態になった後で`WordpressPost`ジョブを自動登録する。
- 自動投稿のWordPress投稿ステータスは`Draft`とする。公開投稿は手動のWordPress投稿APIで行う。
- 同一記事で`WordpressPost`ジョブが`Queued`または`Running`の場合、自動投稿ジョブは重複登録しない。

Response `202 Accepted`:

```json
{
  "createdArticleCount": 3,
  "autoPostToWordpress": true,
  "jobs": [
    {
      "jobId": "2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a",
      "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
      "jobType": "OutlineGeneration",
      "status": "Queued",
      "statusUrl": "/api/jobs/2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a"
    }
  ],
  "rejectedLines": []
}
```

### 6.4 記事詳細取得

```http
GET /api/articles/{articleId}
```

認可: 所有者または管理者。

Response `200 OK`:

```json
{
  "id": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "keyword": "クラヲアクト,ミュージカル",
  "title": "クラヲアクトミュージカルの魅力を徹底解剖！その秘密とは？",
  "status": "Completed",
  "tone": "Normal",
  "tags": ["クラヲアクト", "ミュージカル"],
  "memo": "",
  "body": "",
  "htmlBody": "",
  "metaDescription": "",
  "generationModel": "gemini-3.1-pro-preview",
  "searchMode": true,
  "writingProfileWordpressSiteId": "fb2a11db-849e-475d-8e79-9208e8f6f5af",
  "writingProfileSiteName": "The Mind Journal",
  "createdAt": "2026-01-03T04:00:00Z",
  "updatedAt": "2026-01-03T04:30:00Z",
  "headings": []
}
```

### 6.5 記事更新

```http
PUT /api/articles/{articleId}
```

認可: 所有者または管理者。

Request `UpdateArticleRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `title` | string | 必須 | 1から250文字 |
| `keyword` | string | 必須 | 1から200文字 |
| `tags` | string[] | 任意 | 各50文字以内 |
| `memo` | string | 任意 | 1000文字以内 |
| `metaDescription` | string | 任意 | 320文字以内 |
| `writingProfileWordpressSiteId` | uuid? | 任意 | サイト別ライティング設定を変更する場合に指定。nullで解除 |
| `body` | string | 任意 | 制限は設定で管理 |
| `htmlBody` | string | 任意 | 制限は設定で管理 |
| `rowVersion` | string | 任意 | 同時更新制御用 |

Response `200 OK`: 更新後の`ArticleDetailResponse`。

競合時:

- `rowVersion`が古い場合は`409 Conflict`。
- `writingProfileWordpressSiteId`を変更した場合は、新しいサイト設定を`WritingProfileSnapshotJson`へ再スナップショットする。既存の生成済み本文は自動では再生成しない。
- MVPでは本文履歴を作成しない。`body`と`htmlBody`を指定した場合は現在値を上書きする。

### 6.6 記事削除

```http
DELETE /api/articles/{articleId}
```

認可: 所有者または管理者。

処理:

- `DeletedAt`を設定する論理削除。
- 関連するジョブが`Running`の場合は削除不可。

Response:

- 成功: `204 No Content`
- 実行中ジョブあり: `409 Conflict`

## 7. Headings API

### 7.1 見出し一覧取得

```http
GET /api/articles/{articleId}/headings
```

認可: 所有者または管理者。

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "4e4fd809-803f-46ec-902e-8a61df2e29cc",
      "parentId": null,
      "level": 2,
      "title": "クラヲアクトミュージカルとは？",
      "body": "",
      "displayOrder": 10,
      "targetLength": 500,
      "actualLength": 0,
      "status": "Pending",
      "useWebSearch": true
    }
  ]
}
```

### 7.2 見出し追加

```http
POST /api/articles/{articleId}/headings
```

Request `CreateHeadingRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `parentId` | uuid? | 任意 | H3の場合はH2のID |
| `level` | int | 必須 | 2または3 |
| `title` | string | 必須 | 1から250文字 |
| `insertAfterHeadingId` | uuid? | 任意 | 挿入位置 |
| `targetLength` | int? | 任意 | 0以上 |
| `useWebSearch` | bool | 必須 | 既定false |

Response `201 Created`: `HeadingResponse`。

### 7.3 見出し更新

```http
PUT /api/articles/{articleId}/headings/{headingId}
```

Request `UpdateHeadingRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `title` | string | 必須 | 1から250文字 |
| `body` | string | 任意 | 設定上限内 |
| `targetLength` | int? | 任意 | 0以上 |
| `useWebSearch` | bool | 必須 | 検索利用有無 |
| `rowVersion` | string | 任意 | 同時更新制御用 |

Response `200 OK`: `HeadingResponse`。

### 7.4 見出し削除

```http
DELETE /api/articles/{articleId}/headings/{headingId}
```

処理:

- H2を削除する場合、配下H3も削除する。
- 本文生成中の見出しは削除不可。

Response:

- 成功: `204 No Content`
- 生成中: `409 Conflict`

### 7.5 見出し並び替え

```http
PUT /api/articles/{articleId}/headings/order
```

Request `UpdateHeadingOrderRequest`:

```json
{
  "items": [
    {
      "headingId": "4e4fd809-803f-46ec-902e-8a61df2e29cc",
      "parentId": null,
      "displayOrder": 10
    }
  ]
}
```

Response `200 OK`:

```json
{
  "updated": true
}
```

## 8. Generation API

### 8.1 タイトル候補生成

```http
POST /api/articles/{articleId}/generation/title-candidates
```

認可: 所有者または管理者。

Request `GenerateTitleCandidatesRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `keyword` | string | 必須 | 候補生成の元キーワード |
| `titleMethod` | string | 任意 | `Ai`など |
| `generationModel` | string | 必須 | 使用モデル |
| `candidateCount` | int | 任意 | 既定5、最大20 |

Response `202 Accepted`: `JobAcceptedResponse`。

完了後の結果は`GET /api/jobs/{jobId}`または記事詳細で取得する。

### 8.2 見出し構成生成

```http
POST /api/articles/{articleId}/generation/outline
```

Request `GenerateOutlineRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `h2Count` | int? | 任意 | H2数 |
| `h3Count` | int? | 任意 | H3数 |
| `outlineMethod` | string | 必須 | `Keyword` / `Search` / `Ai` |
| `generationModel` | string | 必須 | 使用モデル |
| `searchMode` | bool | 必須 | Web検索利用 |
| `isDomesticOnly` | bool | 必須 | 日本国内情報限定 |
| `learningType` | string | 任意 | `None` / `Text` / `Url` |
| `learningText` | string | 任意 | 事前学習テキストまたはURL |
| `additionalPrompt` | string | 任意 | 追加指示 |

Response `202 Accepted`: `JobAcceptedResponse`。

### 8.3 見出し本文生成

```http
POST /api/articles/{articleId}/generation/headings/{headingId}/body
```

Request `GenerateHeadingBodyRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `generationModel` | string | 必須 | 使用モデル |
| `targetLength` | int? | 任意 | 文字数目安 |
| `useWebSearch` | bool | 必須 | Web検索利用 |
| `additionalPrompt` | string | 任意 | 追加指示 |

Response `202 Accepted`: `JobAcceptedResponse`。

### 8.4 本文一括生成

```http
POST /api/articles/{articleId}/generation/body
```

Request `GenerateArticleBodyRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `scope` | string | 必須 | `All` / `UnderH3` / `MissingOnly` |
| `generationModel` | string | 必須 | 使用モデル |
| `useWebSearch` | bool | 必須 | Web検索利用 |
| `additionalPrompt` | string | 任意 | 追加指示 |

Response `202 Accepted`: `JobAcceptedResponse`。

### 8.5 本文操作

```http
POST /api/articles/{articleId}/generation/headings/{headingId}/rewrite
```

Request `RewriteHeadingBodyRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `operation` | string | 必須 | `Rewrite` / `Summarize` / `Expand` / `Refresh` |
| `generationModel` | string | 必須 | 使用モデル |
| `additionalPrompt` | string | 任意 | 追加指示 |

Response `202 Accepted`: `JobAcceptedResponse`。

MVPでは本文操作の履歴を作成しない。処理成功時は対象見出し本文の現在値を上書きし、過去版復元APIは提供しない。

### 8.6 HTML変換

```http
POST /api/articles/{articleId}/generation/html
```

Request `ConvertArticleHtmlRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `insertLineBreakAfterPeriod` | bool | 必須 | 句点改行 |

Response `200 OK`:

```json
{
  "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "htmlBody": "<h2>...</h2><p>...</p>",
  "convertedAt": "2026-01-03T04:40:00Z"
}
```

## 9. Jobs API

### 9.1 ジョブ状態取得

```http
GET /api/jobs/{jobId}
```

認可: ジョブ所有者または管理者。

Response `200 OK`:

```json
{
  "id": "2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a",
  "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "headingId": null,
  "jobType": "OutlineGeneration",
  "status": "Succeeded",
  "progress": 100,
  "attemptCount": 1,
  "maxAttempts": 3,
  "errorMessage": null,
  "queuedAt": "2026-01-03T04:00:00Z",
  "startedAt": "2026-01-03T04:00:03Z",
  "finishedAt": "2026-01-03T04:00:40Z",
  "result": {
    "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c"
  }
}
```

### 9.2 記事ジョブ一覧取得

```http
GET /api/articles/{articleId}/jobs
```

Query:

| Query | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `status` | string | 任意 | ジョブ状態 |
| `jobType` | string | 任意 | ジョブ種別 |

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a",
      "jobType": "OutlineGeneration",
      "status": "Succeeded",
      "queuedAt": "2026-01-03T04:00:00Z",
      "finishedAt": "2026-01-03T04:00:40Z"
    }
  ]
}
```

### 9.3 ジョブ再実行

```http
POST /api/jobs/{jobId}/retry
```

認可: ジョブ所有者または管理者。

実行条件:

- 元ジョブが`Failed`である。
- 再実行可能な失敗理由である。
- 利用上限を超過しない。

Response `202 Accepted`: 新しい`JobAcceptedResponse`。

### 9.4 ジョブキャンセル

```http
POST /api/jobs/{jobId}/cancel
```

実行条件:

- `Queued`のみキャンセル可能。
- `Running`のキャンセルは後続フェーズで検討する。

Response `200 OK`:

```json
{
  "id": "2b3a7f7e-5976-4e7e-8c1b-0d3cc15f3e5a",
  "status": "Canceled"
}
```

## 10. Wordpress Sites API

### 10.1 WordPressサイト一覧取得

```http
GET /api/wordpress-sites
```

認可: 認証必須。

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "fb2a11db-849e-475d-8e79-9208e8f6f5af",
      "siteName": "The Mind Journal",
      "baseUrl": "https://example.com",
      "loginId": "AI tools",
      "defaultCategoryId": 16,
      "defaultCategoryName": "MBTI",
      "siteAdminProfile": "The Mind Journal編集部。心理学とエンタメを読みやすく解説する。",
      "writingCharacter": "親しみやすく、少しユーモアのある案内役として書く。",
      "readerPersona": "20代から40代の、心理テストや自己理解に関心がある読者。",
      "createdAt": "2026-01-03T04:00:00Z",
      "updatedAt": "2026-01-03T04:00:00Z"
    }
  ]
}
```

Application Passwordは返さない。

### 10.2 WordPressサイト登録

```http
POST /api/wordpress-sites
```

Request `CreateWordpressSiteRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `siteName` | string | 必須 | 1から100文字 |
| `baseUrl` | string | 必須 | HTTPS URL |
| `loginId` | string | 必須 | 1から100文字 |
| `applicationPassword` | string | 必須 | 1から300文字 |
| `defaultCategoryId` | int? | 任意 | WordPressカテゴリID |
| `siteAdminProfile` | string | 任意 | 管理人プロフィール。2000文字以内 |
| `writingCharacter` | string | 任意 | 語り手・キャラ設定。3000文字以内 |
| `readerPersona` | string | 任意 | 想定読者ペルソナ。3000文字以内 |

Response `201 Created`: `WordpressSiteResponse`。

### 10.3 WordPressサイト更新

```http
PUT /api/wordpress-sites/{wordpressSiteId}
```

Request `UpdateWordpressSiteRequest`:

| プロパティ | 型 | 必須 | 備考 |
| --- | --- | --- | --- |
| `siteName` | string | 必須 | 1から100文字 |
| `baseUrl` | string | 必須 | HTTPS URL |
| `loginId` | string | 必須 | 1から100文字 |
| `applicationPassword` | string | 任意 | 入力時のみ更新 |
| `defaultCategoryId` | int? | 任意 | 既定カテゴリ |
| `siteAdminProfile` | string | 任意 | 管理人プロフィール。2000文字以内 |
| `writingCharacter` | string | 任意 | 語り手・キャラ設定。3000文字以内 |
| `readerPersona` | string | 任意 | 想定読者ペルソナ。3000文字以内 |

Response `200 OK`: `WordpressSiteResponse`。

### 10.4 WordPressサイト削除

```http
DELETE /api/wordpress-sites/{wordpressSiteId}
```

処理:

- 論理削除する。
- 投稿履歴は保持する。

Response `204 No Content`。

### 10.5 カテゴリ取得

```http
GET /api/wordpress-sites/{wordpressSiteId}/categories
```

MVPではDBキャッシュを持たず、リクエストごとにWordPress REST APIからカテゴリ一覧を取得する。
取得したカテゴリ一覧はDBへ保存しない。保存対象は`WordpressSites.DefaultCategoryId` / `DefaultCategoryName`と投稿履歴の`WordpressPosts.CategoryId`のみとする。

Response `200 OK`:

```json
{
  "items": [
    {
      "id": 16,
      "name": "MBTI",
      "slug": "mbti"
    }
  ]
}
```

### 10.6 接続テスト

```http
POST /api/wordpress-sites/{wordpressSiteId}/test
```

Response `200 OK`:

```json
{
  "success": true,
  "message": "WordPressに接続できました。",
  "checkedAt": "2026-01-03T04:00:00Z"
}
```

失敗時は`200 OK`で`success: false`を返す。通信例外などAPI自体が失敗した場合のみ5xxとする。

## 11. Wordpress Posts API

### 11.1 WordPress投稿プレビュー取得

```http
GET /api/articles/{articleId}/wordpress-posts/preview
```

認可: 所有者または管理者。

Response `200 OK`:

```json
{
  "articleId": "7bc3e1d4-8f30-4c21-8873-3f1a47c9d19c",
  "title": "クラヲアクトミュージカルの魅力を徹底解剖！その秘密とは？",
  "htmlBody": "<h2>...</h2><p>...</p>",
  "availableSites": []
}
```

### 11.2 WordPress投稿ジョブ登録

```http
POST /api/articles/{articleId}/wordpress-posts
```

Request `CreateWordpressPostRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `wordpressSiteId` | uuid | 必須 | 投稿先 |
| `title` | string | 必須 | 投稿タイトル |
| `htmlBody` | string | 必須 | 投稿HTML |
| `categoryId` | int? | 任意 | 投稿カテゴリ |
| `status` | string | 任意 | `Draft` / `Publish`。省略時は`Draft` |
| `convertMarkdown` | bool | 任意 | MVPでは使用しない。既定false |

MVPではWordPressメディアアップロードAPIを提供せず、WordPress投稿時のアイキャッチ画像URL指定にも対応しない。
`categoryId`が未指定の場合は投稿先サイトの既定カテゴリを使用する。指定されたカテゴリIDの有効性は投稿時にWordPress側で検証され、無効な場合は投稿失敗として履歴に保存する。

Response `202 Accepted`: `JobAcceptedResponse`。

### 11.3 WordPress投稿履歴取得

```http
GET /api/articles/{articleId}/wordpress-posts
```

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "b272e22a-e64b-42e5-86b7-5d712b0ebfa8",
      "wordpressSiteId": "fb2a11db-849e-475d-8e79-9208e8f6f5af",
      "siteName": "The Mind Journal",
      "postId": 123,
      "postUrl": "https://example.com/posts/sample",
      "status": "Succeeded",
      "errorMessage": null,
      "postedAt": "2026-01-03T04:00:00Z"
    }
  ]
}
```

## 12. Notifications API

MVPの通知Providerは`Discord`固定とし、Discord以外の通知APIは初期実装に含めない。

### 12.1 通知設定取得

```http
GET /api/notifications/settings
```

Response `200 OK`:

```json
{
  "provider": "Discord",
  "destinationMasked": "https://discord.com/api/webhooks/.../...",
  "enabled": true,
  "updatedAt": "2026-01-03T04:00:00Z"
}
```

### 12.2 通知設定保存

```http
PUT /api/notifications/settings
```

Request `UpdateNotificationSettingRequest`:

| プロパティ | 型 | 必須 | 制約 |
| --- | --- | --- | --- |
| `provider` | string | 必須 | `Discord` |
| `destination` | string | 必須 | Discord Webhook URL |
| `enabled` | bool | 必須 | 有効/無効 |

Response `200 OK`: `NotificationSettingResponse`。

### 12.3 通知送信テスト

```http
POST /api/notifications/test
```

Request `SendTestNotificationRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `provider` | string | 必須 | `Discord` |
| `destination` | string | 必須 | Discord Webhook URL |

Response `200 OK`:

```json
{
  "success": true,
  "message": "通知を送信しました。",
  "sentAt": "2026-01-03T04:00:00Z"
}
```

## 13. Usage API

MVPでは月次利用量集計、残り文字数算出、課金計算、利用文字数消費に基づく上限制御は行わない。Usage APIは設定値と利用履歴の確認に限定する。

### 13.1 利用設定概要取得

```http
GET /api/usage/summary
```

認可: 認証必須。

Response `200 OK`:

```json
{
  "monthlyLimitChars": 200000,
  "remainingOutlineCount": 40,
  "defaultModel": "gemini-3.1-pro-preview",
  "monthlyUsageAggregationEnabled": false
}
```

### 13.2 利用履歴取得

```http
GET /api/usage/ledgers
```

Query:

| Query | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `page` | int | 任意 | ページ番号 |
| `pageSize` | int | 任意 | 件数 |
| `from` | date | 任意 | 期間From |
| `to` | date | 任意 | 期間To |

Response `200 OK`: `PagedResult<UsageLedgerResponse>`。

`GET /api/usage/ledgers`は個別履歴のみ返す。MVPでは期間合計、月次合計、残り文字数は返さない。

## 14. Account API

### 14.1 本人退会

```http
DELETE /api/account
```

認可: 認証必須。

Request `WithdrawAccountRequest`:

| プロパティ | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `currentPassword` | string | 必須 | 現在パスワード |
| `confirmText` | string | 必須 | `DELETE` |

処理:

- ログインユーザー本人を対象にする。
- `currentPassword`で再確認する。パスワードはログ、監査ログ、レスポンスへ出さない。
- 最後のAdminユーザーの場合は拒否する。
- 対象ユーザーに`Running`ジョブがある場合は退会を拒否する。
- 対象ユーザーに紐づく記事、見出し、ジョブ、AI生成ログ、利用台帳、Tavily検索結果、X投稿キャッシュ、WordPressサイト、WordPress投稿履歴、通知設定、通知ログ、利用上限、対象ユーザーが操作ユーザーの監査ログをトランザクション内で物理削除する。
- ユーザー本体はASP.NET Core Identityの`UserManager`を通して削除する。
- 退会監査ログは削除対象ユーザーへのFKを持たず、対象ユーザーIDを文字列スナップショットとして保存する。`UserId`はNULLにする。
- 削除成功後、認証Cookieを破棄する。

Response:

| Status | 条件 |
| --- | --- |
| `204 No Content` | 退会成功 |
| `400 Bad Request` | パスワード不一致、確認文字列不一致、最後のAdminユーザー |
| `409 Conflict` | 対象ユーザーにRunningジョブが存在する |

## 15. Admin API

管理者APIは外部公開対象に含めない。MVPでは、ユーザー運用に必要な以下の範囲に限定する。

- ユーザー一覧、ユーザー作成、表示名と有効状態の更新
- `User` / `Admin`ロール変更
- ユーザー別利用上限更新
- 管理者によるユーザー物理削除
- ユーザー管理監査ログの参照

MVPでは以下を管理者APIに含めない。

- 管理者による他ユーザーの記事、WordPress連携、通知設定の代理編集
- AIモデル設定の更新
- strict判定辞書の更新
- ジョブの強制停止、強制再実行、全体キュー操作
- 外部連携設定の管理者一括変更

### 15.1 ユーザー一覧

```http
GET /api/admin/users
```

認可: 管理者。

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "user-id",
      "email": "user@example.com",
      "displayName": "User",
      "role": "User",
      "isEnabled": true,
      "monthlyLimitChars": 200000,
      "remainingOutlineCount": 40
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

### 15.2 ユーザー作成

```http
POST /api/admin/users
```

認可: 管理者。

Request:

```json
{
  "email": "new-user@example.com",
  "displayName": "New User",
  "password": "initial-password",
  "role": "User",
  "isEnabled": true,
  "monthlyLimitChars": 200000,
  "remainingOutlineCount": 40
}
```

処理:

- ASP.NET Core Identityの`UserManager`でユーザーを作成する。
- `RoleManager`で`Admin`または`User`ロールの存在を確認する。
- 指定ロールを付与する。未指定時は`User`とする。
- 作成者、対象ユーザーID、付与ロールを監査ログへ記録する。
- パスワードはログ、監査ログ、レスポンスに含めない。

Response `201 Created`:

```json
{
  "id": "user-id",
  "email": "new-user@example.com",
  "displayName": "New User",
  "role": "User",
  "isEnabled": true
}
```

### 15.3 ユーザー更新

```http
PUT /api/admin/users/{userId}
```

認可: 管理者。

Request:

```json
{
  "displayName": "Updated User",
  "isEnabled": true
}
```

処理:

- 対象ユーザーの存在を確認する。
- MVPではメールアドレス変更とパスワード再設定は扱わない。
- 最後のAdminユーザーの無効化は拒否する。
- 変更前後の有効状態、実行者を監査ログへ記録する。

Response `200 OK`:

```json
{
  "id": "user-id",
  "email": "user@example.com",
  "displayName": "Updated User",
  "role": "User",
  "isEnabled": true
}
```

### 15.4 ユーザーロール変更

```http
PUT /api/admin/users/{userId}/role
```

認可: 管理者。

Request:

```json
{
  "role": "Admin"
}
```

処理:

- 対象ユーザーの存在を確認する。
- `Admin`または`User`のみ許可する。
- `User`へ降格する場合、最後のAdminユーザーであれば拒否する。
- `UserManager` / `RoleManager`を使ってロールを変更する。
- 変更前ロール、変更後ロール、実行者を監査ログへ記録する。

Response:

| ステータス | 条件 |
| --- | --- |
| `200 OK` | 変更成功 |
| `400 Bad Request` | 最後のAdminユーザーを降格しようとした |
| `404 Not Found` | 対象ユーザーが存在しない |

### 15.5 ユーザー利用上限更新

```http
PUT /api/admin/users/{userId}/usage-limit
```

認可: 管理者。

Request:

```json
{
  "monthlyLimitChars": 200000,
  "remainingOutlineCount": 40
}
```

処理:

- 対象ユーザーの存在を確認する。
- `monthlyLimitChars`は0以上を許可する。0は新規ジョブ登録不可として扱う。
- 変更前後の上限値、実行者を監査ログへ記録する。

Response `200 OK`。

### 15.6 ユーザー削除

```http
DELETE /api/admin/users/{userId}
```

認可: 管理者。

処理:

- 対象ユーザーの存在を確認する。
- 管理者自身の削除は拒否する。
- 最後のAdminユーザーの削除は拒否する。
- 対象ユーザーに`Running`ジョブがある場合は削除を拒否する。
- 対象ユーザーに紐づく記事、見出し、ジョブ、AI生成ログ、利用台帳、Tavily検索結果、X投稿キャッシュ、WordPressサイト、WordPress投稿履歴、通知設定、通知ログ、利用上限、対象ユーザーが操作ユーザーの監査ログをトランザクション内で物理削除する。
- ユーザー本体はASP.NET Core Identityの`UserManager`を通して削除する。
- 削除実行者、対象ユーザーID、削除件数サマリを監査ログへ記録する。ただし削除対象ユーザーへのFKは持たず、文字列スナップショットとして保存する。

Response:

| ステータス | 条件 |
| --- | --- |
| `204 No Content` | 削除成功 |
| `400 Bad Request` | 自分自身または最後のAdminユーザーを削除しようとした |
| `404 Not Found` | 対象ユーザーが存在しない |
| `409 Conflict` | 対象ユーザーにRunningジョブが存在する |

### 15.7 ユーザー管理監査ログ

```http
GET /api/admin/audit-logs
```

認可: 管理者。

Query:

| Query | 型 | 必須 | 説明 |
| --- | --- | --- | --- |
| `page` | int | 任意 | ページ番号 |
| `pageSize` | int | 任意 | 1から100 |
| `targetUserId` | string | 任意 | 対象ユーザーID |
| `action` | string | 任意 | `UserCreated` / `UserUpdated` / `RoleChanged` / `UsageLimitUpdated` / `UserDeleted` |
| `from` | date | 任意 | 期間From |
| `to` | date | 任意 | 期間To |

Response `200 OK`:

```json
{
  "items": [
    {
      "id": "2f8127c5-6a6a-4cd8-b379-944e62e3e481",
      "action": "RoleChanged",
      "adminUserId": "admin-user-id",
      "targetUserId": "user-id",
      "targetUserIdSnapshot": null,
      "summary": "Role changed from User to Admin.",
      "createdAt": "2026-01-03T04:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 1,
  "totalPages": 1,
  "hasPrevious": false,
  "hasNext": false
}
```

## 16. 入力バリデーション

### 16.1 文字列制限

| 項目 | 最大長 |
| --- | --- |
| キーワード | 200 |
| 記事タイトル | 250 |
| タグ | 50 |
| メモ | 1000 |
| メタディスクリプション | 320 |
| 追加プロンプト | 3000 |
| WordPressサイト名 | 100 |
| WordPressログインID | 100 |
| 管理人プロフィール | 2000 |
| 語り手・キャラ設定 | 3000 |
| 読者ペルソナ | 3000 |

### 16.2 URL検証

- WordPressサイトURLはHTTPSのみ許可する。
- 事前学習URLはHTTPSのみ許可する。
- localhost、プライベートIP、リンクローカル、メタデータIPへのアクセスは禁止する。

### 16.3 業務バリデーション

- 利用上限超過時はジョブ登録を拒否する。
- 投稿可能な記事ステータスは`Completed`または`Posted`とする。
- `Running`ジョブが存在する記事は削除不可とする。
- `Running`ジョブが存在するユーザーは管理者削除不可とする。
- 管理者自身と最後のAdminユーザーは削除不可とする。
- 最後のAdminユーザーは降格、無効化不可とする。
- H3の`parentId`は同一記事内のH2でなければならない。
- WordPress Application Passwordは登録/更新時のみ受け取り、レスポンスには含めない。
- 記事作成・一括作成で指定する`writingProfileWordpressSiteId`は、同一ユーザー所有の有効なWordPressサイトでなければならない。
- 一括作成のWordPress自動投稿は、投稿先サイトが同一ユーザー所有で、記事が`Completed`になりHTML本文が存在する場合のみ下書きジョブを登録する。

## 17. OpenAPI方針

- Minimal APIのエンドポイントには`WithName`、`WithSummary`、`WithDescription`を設定する。
- DTOにはXMLコメントまたはOpenAPI用メタデータを付与する。
- OpenAPIは開発・内部確認用に生成し、外部向けAPI仕様としては扱わない。
- 管理者API、内部APIもOpenAPIに出すが、本番では認証必須とする。
- APIバージョニングはMVPではURLに含めない。外部公開が必要になった段階で`/api/v1`を導入する。

## 18. 実装時のルート定義例

```csharp
public static class ArticleEndpoints
{
    public static RouteGroupBuilder MapArticleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/articles")
            .RequireAuthorization()
            .WithTags("Articles");

        group.MapGet("/", GetArticles)
            .WithName("GetArticles")
            .WithSummary("記事一覧を取得します。");

        group.MapPost("/", CreateArticle)
            .WithName("CreateArticle")
            .WithSummary("記事を作成します。");

        group.MapGet("/{articleId:guid}", GetArticle)
            .WithName("GetArticle")
            .WithSummary("記事詳細を取得します。");

        return group;
    }
}
```

ハンドラーでは業務処理を直接書かず、Applicationサービスへ委譲する。

## 19. テスト観点

- 未認証時に`401`が返る。
- 他ユーザーの記事操作で`403`または`404`が返る。
- 入力不正でProblemDetailsが返る。
- 記事作成APIで記事が作成される。
- 記事作成APIでサイト別ライティング設定を指定した場合、所有者チェックとスナップショット保存が行われる。
- 一括作成APIで複数記事とジョブが作成される。
- ジョブ登録APIが`202 Accepted`を返す。
- ジョブ状態取得APIで所有者チェックが行われる。
- WordPress Application Passwordがレスポンスに含まれない。
- WordPress接続テスト失敗時に秘密情報がレスポンスへ含まれない。
- 利用上限超過時にジョブ登録が拒否される。
- 一括作成APIでWordPress自動投稿を有効にした場合、投稿先サイトの所有者確認が行われ、本文生成完了後に下書き投稿ジョブが重複なく登録される。
