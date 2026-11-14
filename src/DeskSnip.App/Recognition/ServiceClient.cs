using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using DeskSnip.Core;

namespace DeskSnip.App.Recognition;

internal sealed class ServiceClient : IDisposable
{
    private const int MaximumResponseBytes = 40 * 1024 * 1024;
    private readonly AppSettings settings;
    private readonly ServiceCredentialStore credentials;
    private readonly HttpClient client;
    internal ServiceClient(AppSettings settings, string dataRoot, HttpMessageHandler? handler = null)
    {
        settings.Validate(); this.settings = settings.Clone(); credentials = new ServiceCredentialStore(dataRoot);
        client = new HttpClient(handler ?? CreateHandler(settings)) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static HttpClientHandler CreateHandler(AppSettings settings)
    {
        settings.Validate();
        return new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = settings.NetworkProxyMode != "None", Proxy = settings.NetworkProxyMode == "Http" ? new WebProxy(new UriBuilder("http", settings.NetworkProxyHost, settings.NetworkProxyPort).Uri) : null };
    }
    internal async Task<RecognitionResponse> RecognizeAsync(RecognitionRequest request, CancellationToken cancellation)
    {
        request.Validate(); var endpoint = RecognitionEndpoint.Validate(settings.RecognitionServiceEndpoint);
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        using (var message = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.RecognitionTimeoutSeconds));
            using (var serialized = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(RecognitionRequest)).WriteObject(serialized, request);
                message.Content = new ByteArrayContent(serialized.ToArray());
            }
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var key = credentials.Resolve(settings.RecognitionKeyEnvironment);
            if (key.Length > 8192 || key.Length > 0 && !Regex.IsMatch(key, @"^[A-Za-z0-9._~+/=-]+$", RegexOptions.CultureInvariant)) throw new ArgumentException(L.Get("访问密钥格式无效，请检查本机密钥或环境变量。"));
            if (!string.IsNullOrEmpty(key)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            try
            {
                using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(L.Format("服务请求失败（HTTP {0}）。原图和旧结果保留，请检查地址、密钥及服务状态。",(int)response.StatusCode));
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidOperationException(L.Get("服务结果过大，原图和旧结果保留。"));
                    using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var buffer = new MemoryStream())
                    {
                        var bytes = new byte[8192]; int count;
                        while ((count = await input.ReadAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false)) > 0)
                        {
                            if (buffer.Length + count > MaximumResponseBytes) throw new InvalidOperationException(L.Get("服务结果过大，原图和旧结果保留。"));
                            buffer.Write(bytes, 0, count);
                        }
                        timeout.Token.ThrowIfCancellationRequested(); buffer.Position = 0;
                        RecognitionResponse result;
                        try { result = (RecognitionResponse)(new DataContractJsonSerializer(typeof(RecognitionResponse)).ReadObject(buffer) ?? throw new ArgumentException(L.Get("服务结果为空。"))); }
                        catch (System.Runtime.Serialization.SerializationException) { throw new ArgumentException(L.Get("服务需要返回 DeskSnip 识别协议的 JSON，原结果保留。")); }
                        result.Validate(request.Operation);
                        foreach (var asset in result.Assets) ServiceImages.Decode(asset.Base64);
                        return result;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new TimeoutException(L.Get("服务响应超时，原图和旧结果保留，可重试或延长超时。")); }
            catch (HttpRequestException) { throw new InvalidOperationException(L.Get("无法连接识别服务，原图和旧结果保留。请检查服务地址和网络。")); }
        }
    }
    public void Dispose() => client.Dispose();
}
