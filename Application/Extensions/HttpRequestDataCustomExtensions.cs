using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json;
using System.Net;

namespace Elysian.Application.Extensions
{
    public static class HttpRequestDataCustomExtensions
    {
        public static async Task<HttpResponseData> WriteJsonResponseAsync<T>(this HttpRequestData req, T data, HttpStatusCode httpStatusCode = HttpStatusCode.OK)
        {
            var response = req.CreateResponse(httpStatusCode);
            response.Headers.Add("Content-Type", "text/json; charset=utf-8");
            await response.WriteStringAsync(JsonConvert.SerializeObject(data));
            return response;
        }

        public static async Task<HttpResponseData> WriteHtmlResponseAsync(this HttpRequestData req, string html, HttpStatusCode httpStatusCode = HttpStatusCode.OK)
        {
            var response = req.CreateResponse(httpStatusCode);
            response.Headers.Add("Content-Type", "text/html; charset=utf-8");
            await response.WriteStringAsync(html);
            return response;
        }

        public static async Task<HttpResponseData> WriteFailureResponseAsync(this HttpRequestData req, HttpStatusCode httpStatusCode, string errorMessage)
        {
            var response = req.CreateResponse(httpStatusCode);
            response.Headers.Add("Content-Type", "text/json; charset=utf-8");
            await response.WriteStringAsync(JsonConvert.SerializeObject(new
            {
                Error = errorMessage
            }));
            return response;
        }

        public static async Task<T> DeserializeBodyAsync<T>(this HttpRequestData req)
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonConvert.DeserializeObject<T>(body);
        }

        public static bool TryGetEnumValue<T>(this HttpRequestData req, string name, out T result) where T : struct
        {
            return Enum.TryParse(req.Query[name], true, out result);
        }
    }
}
