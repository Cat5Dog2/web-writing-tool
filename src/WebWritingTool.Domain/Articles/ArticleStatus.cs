namespace WebWritingTool.Domain.Articles;

public enum ArticleStatus
{
    Draft,
    OutlineQueued,
    OutlineGenerating,
    OutlineReady,
    BodyQueued,
    BodyGenerating,
    Completed,
    Posted,
    Failed
}
