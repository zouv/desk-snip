using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class RecognitionServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-service-" + Guid.NewGuid().ToString("N"));
    internal static string SyntheticImage()
    {
        string encoded = "";
        ImagingTests.Sta(() => encoded = ServiceImages.Encode(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16).ToArray(), 8)));
        return encoded;
    }
    internal static RecognitionResponse Result(string operation)
        => new RecognitionResponse { Operation = operation, Text = "Sample source", TranslatedText = "示例译文", Markdown = "# 示例\n\n| 名称 | 值 |\n| --- | --- |\n| Sample | 10 |\n\n$$x^2+1$$", Formulas = new List<string> { "x^2+1", "\\frac{1}{2}" }, Table = new StructuredTable { Rows = new List<List<string>> { new List<string> { "名称", "数量" }, new List<string> { "示例", "10" } } } };
    private static byte[] Json<T>(T value) { using (var buffer = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(buffer, value); return buffer.ToArray(); } }
    [Theory]
    [InlineData("text")][InlineData("translation")][InlineData("table")][InlineData("formula")][InlineData("document")]
    public async Task LoopbackServiceReceivesExplicitImageLanguageAndBearerRequest(string operation)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var credential = new ServiceCredentialStore(root); credential.Write("synthetic-test-key");
        var payload = new RecognitionRequest { Operation = operation, ImageBase64 = SyntheticImage(), SourceLanguage = "en", TargetLanguage = "zh" };
        var receivedHeader = ""; RecognitionRequest? received = null;
        var server = Task.Run(async () =>
        {
            using (var connection = await listener.AcceptTcpClientAsync())
            using (var stream = connection.GetStream())
            {
                var header = new List<byte>(); var single = new byte[1];
                while (header.Count < 65536)
                {
                    if (await stream.ReadAsync(single, 0, 1) == 0) throw new IOException("Request ended before headers."); header.Add(single[0]);
                    if (header.Count > 3 && header.Skip(header.Count - 4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                }
                receivedHeader = Encoding.ASCII.GetString(header.ToArray());
                var length = int.Parse(receivedHeader.Split(new[] { "\r\n" }, StringSplitOptions.None).Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
                var bytes = new byte[length]; var offset = 0;
                while (offset < length) { var count = await stream.ReadAsync(bytes, offset, length - offset); if (count == 0) throw new IOException("Request body incomplete."); offset += count; }
                using (var input = new MemoryStream(bytes)) received = (RecognitionRequest)new DataContractJsonSerializer(typeof(RecognitionRequest)).ReadObject(input)!;
                var response = Json(Result(operation)); var status = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " + response.Length + "\r\n\r\n");
                await stream.WriteAsync(status, 0, status.Length); await stream.WriteAsync(response, 0, response.Length);
            }
        });
        try
        {
            using (var client = new ServiceClient(new AppSettings { RecognitionServiceEndpoint = $"http://127.0.0.1:{port}/recognize" }, root))
            using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var result = await client.RecognizeAsync(payload, limit.Token); await server;
                Assert.Equal(operation, result.Operation); Assert.StartsWith("POST /recognize HTTP/1.1", receivedHeader); Assert.Contains("Authorization: Bearer synthetic-test-key", receivedHeader); Assert.DoesNotContain("Cookie:", receivedHeader);
                Assert.Equal(payload.ImageBase64, received!.ImageBase64); Assert.Equal("en", received.SourceLanguage); Assert.Equal("zh", received.TargetLanguage);
            }
        }
        finally { listener.Stop(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Response(request, cancellationToken);
    }
    [Fact] public async Task MalformedFailureAndCancellationKeepSavedResultAndImage()
    {
        var store = new RecognitionResultStore(root); var session = new StoredRecognitionSession { Result = Result("text"), ImageBase64 = SyntheticImage(), RequestText = "original request" }; store.SaveSession(session);
        foreach (var response in new[] { new HttpResponseMessage(HttpStatusCode.Unauthorized), new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.com/redirect") } }, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") }, new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Json(Result("table"))) } })
        {
            var handler = new Handler { Response = (_, _) => Task.FromResult(response) };
            using (var client = new ServiceClient(new AppSettings { RecognitionServiceEndpoint = "http://127.0.0.1/recognize" }, root, handler)) await Assert.ThrowsAnyAsync<Exception>(() => client.RecognizeAsync(new RecognitionRequest { Text = "synthetic request" }, CancellationToken.None));
            Assert.Equal("Sample source", store.Read()!.Text); Assert.Equal(session.ImageBase64, store.ReadSession()!.ImageBase64);
        }
        var entered = new TaskCompletionSource<bool>(); var waiting = new Handler { Response = async (_, token) => { entered.SetResult(true); await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); } };
        using (var cancellation = new CancellationTokenSource())
        using (var client = new ServiceClient(new AppSettings { RecognitionServiceEndpoint = "http://127.0.0.1/recognize" }, root, waiting))
        { var operation = client.RecognizeAsync(new RecognitionRequest { Text = "synthetic request" }, cancellation.Token); await entered.Task; cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation); }
        Assert.Equal("original request", store.ReadSession()!.RequestText);
        var oversized = new Handler { Response = (_, _) => { var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) }; message.Content.Headers.ContentLength = 50 * 1024 * 1024; return Task.FromResult(message); } };
        using (var client = new ServiceClient(new AppSettings { RecognitionServiceEndpoint = "http://127.0.0.1/recognize" }, root, oversized)) await Assert.ThrowsAsync<InvalidOperationException>(() => client.RecognizeAsync(new RecognitionRequest { Text = "synthetic request" }, CancellationToken.None));
        Assert.Equal("Sample source", store.Read()!.Text);
    }
    [Fact] public void CredentialsAreEncryptedAndExcludedFromPreferenceExport()
    {
        var store = new ServiceCredentialStore(root); store.Write("synthetic-test-key"); Assert.Equal("synthetic-test-key", store.Read());
        Assert.DoesNotContain("synthetic-test-key", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "credentials", "recognition.dat"))));
        var variable = "DESK_SNIP_TEST_KEY_" + Guid.NewGuid().ToString("N");
        try { Environment.SetEnvironmentVariable(variable, "synthetic-env-key"); Assert.Equal("synthetic-env-key", store.Resolve(variable)); }
        finally { Environment.SetEnvironmentVariable(variable, null); }
        var preferences = PreferenceTransfer.Export(new AppSettings { RecognitionServiceEndpoint = "https://example.com/recognize", RecognitionKeyEnvironment = variable, RecognitionTargetLanguage = "en" });
        Assert.False(preferences.Values.ContainsKey(nameof(AppSettings.RecognitionServiceEndpoint))); Assert.False(preferences.Values.ContainsKey(nameof(AppSettings.RecognitionKeyEnvironment))); Assert.Equal("en", preferences.Values[nameof(AppSettings.RecognitionTargetLanguage)]);
        Assert.Throws<ArgumentException>(() => store.Write("bad\nkey")); Assert.Equal("synthetic-test-key", store.Read()); store.Write(""); Assert.Equal("", store.Read());
    }
    [Fact] public async Task ServiceTimeoutIsReportedSeparatelyFromUserCancellation()
    {
        var waiting = new Handler { Response = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); } };
        using (var client = new ServiceClient(new AppSettings { RecognitionServiceEndpoint = "http://127.0.0.1/recognize", RecognitionTimeoutSeconds = 5 }, root, waiting))
            await Assert.ThrowsAsync<TimeoutException>(() => client.RecognizeAsync(new RecognitionRequest { Text = "synthetic request" }, CancellationToken.None));
    }
    [Fact] public void ReplacingUnreadableCredentialUsesEncryptedRollbackWithoutDecryption()
    {
        var credentials = new ServiceCredentialStore(root); credentials.Write("synthetic-old-key"); var path = Path.Combine(root, "credentials", "recognition.dat"); var opaque = new byte[] { 1, 2, 3, 4 }; File.WriteAllBytes(path, opaque);
        Assert.Throws<IOException>(() => credentials.Change("synthetic-new-key", () => throw new IOException("Synthetic configuration failure."))); Assert.Equal(opaque, File.ReadAllBytes(path));
        credentials.Change("synthetic-new-key", () => { }); Assert.Equal("synthetic-new-key", credentials.Read());
    }
    [Theory]
    [InlineData("http://example.com/api")][InlineData("https://name:pass@example.com")][InlineData("https://example.com/#key")][InlineData("file:///example")]
    public void EndpointRejectsPlainRemoteHttpAndEmbeddedCredentials(string address) => Assert.Throws<ArgumentException>(() => RecognitionEndpoint.Validate(address));
    [Fact] public void InvalidTablesAssetsAndResultsDoNotReplacePreviousSession()
    {
        var store = new RecognitionResultStore(root); store.Save(Result("text")); var bad = Result("document");
        bad.Assets.Add(new RecognitionAsset { Name = "../escape.png", Base64 = SyntheticImage() }); Assert.Throws<ArgumentException>(() => store.Save(bad)); Assert.Equal("text", store.Read()!.Operation);
        bad.Assets[0].Name = "safe.png"; bad.Assets[0].Base64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }); Assert.Throws<ArgumentException>(() => bad.Validate());
        var empty = new RecognitionResponse(); Assert.Throws<ArgumentException>(() => store.Save(empty)); Assert.Equal("text", store.Read()!.Operation);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
