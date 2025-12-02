using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ActionFilters.Filters;

public class ExecutionLoggingFilter(ILogger<ExecutionLoggingFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext? executedContext = null;
        var actionName = context.ActionDescriptor.DisplayName ?? context.ActionDescriptor.Id;

        try
        {
            executedContext = await next();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            HandleException(actionName, ex, stopwatch.ElapsedMilliseconds, context, executedContext: null);
            return;
        }

        stopwatch.Stop();

        if (executedContext is { Exception: { } exception, ExceptionHandled: false })
        {
            HandleException(actionName, exception, stopwatch.ElapsedMilliseconds, context, executedContext);
            return;
        }

        logger.LogInformation("Executed {ActionName} in {ElapsedMilliseconds} ms", actionName, stopwatch.ElapsedMilliseconds);
    }

    private void HandleException(string actionName, Exception exception, long elapsedMilliseconds, ActionExecutingContext executingContext, ActionExecutedContext? executedContext)
    {
        logger.LogError(exception, "Exception executing {ActionName} after {ElapsedMilliseconds} ms", actionName, elapsedMilliseconds);

        var errorResult = new ObjectResult(new { Message = "An error occurred while processing your request." })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };

        if (executedContext != null)
        {
            executedContext.ExceptionHandled = true;
            executedContext.Result = errorResult;
            return;
        }

        executingContext.Result = errorResult;
    }
}
