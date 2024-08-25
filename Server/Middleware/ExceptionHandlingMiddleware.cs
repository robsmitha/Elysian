using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Elysian.Application.Exceptions;
using Newtonsoft.Json;

namespace Elysian.Middleware
{
    /// <summary>
    /// This middleware catches any exceptions during function invocations and
    /// returns a response with 500 status code for http invocations.
    /// </summary>
    internal sealed class ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger) : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<ExceptionHandlingMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invocation");

                var httpReqData = await context.GetHttpRequestDataAsync();

                if (httpReqData != null)
                {
                    var code = HttpStatusCode.InternalServerError;

                    var result = string.Empty;

                    switch (ex)
                    {
                        case CustomValidationException validationException:
                            code = HttpStatusCode.BadRequest;
                            result = JsonConvert.SerializeObject(
                                validationException.Errors);
                            break;
                        case FinancialServiceException financialServiceException:
                            code = HttpStatusCode.BadRequest;
                            result = JsonConvert.SerializeObject(
                                financialServiceException.Error);
                            break;
                        case NotFoundException _:
                            code = HttpStatusCode.NotFound;
                            break;
                        case ForbiddenAccessException _:
                            code = HttpStatusCode.Forbidden;
                            break;
                    }

                    if (string.IsNullOrEmpty(result))
                    {
                        result = JsonConvert.SerializeObject(new { error = "An unhandled error occurred processing the request." });
                    }

                    var newHttpResponse = httpReqData.CreateResponse(code);
                    // You need to explicitly pass the status code in WriteAsJsonAsync method.
                    // https://github.com/Azure/azure-functions-dotnet-worker/issues/776
                    await newHttpResponse.WriteAsJsonAsync(result, newHttpResponse.StatusCode);

                    var invocationResult = context.GetInvocationResult();

                    var httpOutputBindingFromMultipleOutputBindings = GetHttpOutputBindingFromMultipleOutputBinding(context);
                    if (httpOutputBindingFromMultipleOutputBindings is not null)
                    {
                        httpOutputBindingFromMultipleOutputBindings.Value = newHttpResponse;
                    }
                    else
                    {
                        invocationResult.Value = newHttpResponse;
                    }
                }
            }
        }

        private OutputBindingData<HttpResponseData> GetHttpOutputBindingFromMultipleOutputBinding(FunctionContext context)
        {
            // The output binding entry name will be "$return" only when the function return type is HttpResponseData
            var httpOutputBinding = context.GetOutputBindings<HttpResponseData>()
                .FirstOrDefault(b => b.BindingType == "http" && b.Name != "$return");

            return httpOutputBinding;
        }
    }
}
