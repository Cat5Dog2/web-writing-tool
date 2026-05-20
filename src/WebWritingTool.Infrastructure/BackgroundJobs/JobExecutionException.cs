namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class JobExecutionException : Exception
{
    public JobExecutionException(
        string errorCode,
        string userMessage,
        Exception? innerException = null,
        TimeSpan? retryAfter = null)
        : base(userMessage, innerException)
    {
        ErrorCode = errorCode;
        UserMessage = userMessage;
        RetryAfter = retryAfter;
    }

    public string ErrorCode { get; }

    public string UserMessage { get; }

    public TimeSpan? RetryAfter { get; }
}
