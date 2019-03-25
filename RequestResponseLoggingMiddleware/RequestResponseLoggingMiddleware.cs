using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RequestResponseLoggingMiddleware
{
    public class RequestResponseLoggingMiddleware
    {
        private readonly string Path = "/log";

        public static bool IsLogFileEnabled => !string.IsNullOrEmpty(LogFile);

        internal static string LogFile = null;

        private readonly RequestDelegate _next;
        private readonly ILogger _logger;

        public RequestResponseLoggingMiddleware(RequestDelegate next,
                                                ILoggerFactory loggerFactory)
        {
            this._next = next;
            this._logger = loggerFactory
                      .CreateLogger<RequestResponseLoggingMiddleware>();
        }

        /// <summary>
        /// the method that is in charge of the function of this middleware
        /// </summary>
        public async Task Invoke(HttpContext context)
        {
            string response;
            string request = await this.FormatRequest(context.Request);
            this._logger.LogInformation(request);

            if (IsLogFileEnabled && context.Request.Path.Equals(this.Path))
            {
                await ServeLogFile(context);

                // to stop futher pipeline execution 
                return;
            }

            Stream originalBodyStream = context.Response.Body;

            using (MemoryStream responseBody = new MemoryStream())
            {
                context.Response.Body = responseBody;

                await this._next(context);
                response = await this.FormatResponse(context.Response);
                this._logger.LogInformation(response);
                await responseBody.CopyToAsync(originalBodyStream);
            }

            if (!string.IsNullOrEmpty(LogFile))
            {
                await this.WriteDownLog(request, response);
            }
        }

        #region Helper method

        /// <summary>
        /// Serve the log file
        /// </summary>
        private static async Task ServeLogFile(HttpContext context)
        {
            context.Response.StatusCode = 200;

            context.Response.ContentType = "text/plain";

            using (FileStream fs = File.Open(LogFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    string log = await sr.ReadToEndAsync();
                    await context.Response.WriteAsync(log, Encoding.UTF8);
                }
            }
        }

        /// <summary>
        /// write down the request and response into a log file
        /// </summary>
        /// <param name="request">the received request</param>
        /// <param name="response">the produced response</param>
        private async Task WriteDownLog(string request, string response)
        {
            using (FileStream fs = File.Open(LogFile, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    await sw.WriteLineAsync(string.Format("Timestamp:{0}", DateTime.Now));

                    await sw.WriteLineAsync("Request:");
                    await sw.WriteLineAsync(request);

                    await sw.WriteLineAsync("Response:");
                    await sw.WriteLineAsync(response);

                    await sw.WriteLineAsync("==========");
                }
            }
        }

        /// <summary>
        /// pretty print the request
        /// </summary>
        /// <param name="request">the received request</param>
        private async Task<string> FormatRequest(HttpRequest request)
        {
            Stream body = request.Body;
            request.EnableRewind();

            byte[] buffer = new byte[Convert.ToInt32(request.ContentLength)];
            await request.Body.ReadAsync(buffer, 0, buffer.Length);
            string bodyAsText = Encoding.UTF8.GetString(buffer);
            request.Body = body;

            return $"{request.Scheme} {request.Host}{request.Path} {request.QueryString} {bodyAsText}";
        }

        /// <summary>
        /// pretty print the response
        /// </summary>
        /// <param name="response">the produced response</param>
        private async Task<string> FormatResponse(HttpResponse response)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            string text = await new StreamReader(response.Body).ReadToEndAsync();
            response.Body.Seek(0, SeekOrigin.Begin);

            return $"Response {text}";
        }

        #endregion
    }

    public static class RequestResponseLoggingMiddlewareExtensions
    {
        public static IServiceCollection AddFileRequestResponseLogging(this IServiceCollection services, string logfile = null)
        {
            RequestResponseLoggingMiddleware.LogFile = logfile;
            return services;
        }

        /// <summary>
        /// Register the logging middleware into the pipeline
        /// </summary>
        public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestResponseLoggingMiddleware>();
        }
    }
}
