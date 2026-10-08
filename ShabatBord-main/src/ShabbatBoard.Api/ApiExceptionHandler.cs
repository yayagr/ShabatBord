using Microsoft.AspNetCore.Diagnostics;
using ShabbatBoard.Application;
using ShabbatBoard.Domain;

namespace ShabbatBoard.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, detail) = exception switch
        {
            RuleException rule => (rule.Failure switch
            {
                RuleFailure.NotFound => 404,
                RuleFailure.Conflict => 409,
                _ => 400
            }, rule.Message),
            BadHttpRequestException => (400, "הבקשה אינה תקינה. יש לבדוק את הנתונים ולנסות שוב."),
            UpstreamException => (502, "לא ניתן לקבל כרגע נתוני לוח שנה מ־Hebcal. נסו שוב בעוד רגע."),
            IOException or UnauthorizedAccessException =>
                (503, "לא ניתן לגשת לקובץ השיבוצים. השינוי לא נשמר; יש לפנות למנהל המערכת."),
            _ => (500, "אירעה שגיאה בלתי צפויה. נסו שוב או פנו למנהל המערכת.")
        };
        if (status >= 500)
            logger.LogError("Request failed with {Status}, error type {ErrorType}, trace {TraceId}.",
                status, exception.GetType().Name, context.TraceIdentifier);
        else
            logger.LogWarning("Request rejected with {Status}, trace {TraceId}.", status, context.TraceIdentifier);

        await Results.Problem(statusCode: status, title: "הבקשה לא הושלמה", detail: detail,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return true;
    }
}
