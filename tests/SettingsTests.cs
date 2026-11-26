using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Shiyi
{
    internal static class SettingsTests
    {
        private static void Check(bool valid, string label) { if (!valid) throw new Exception(label); }

        private sealed class Handler : HttpMessageHandler
        {
            public readonly List<Uri> Requests = new List<Uri>();
            public Func<int, HttpResponseMessage> Reply;
            public bool SentKey;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Check(request.Method == HttpMethod.Get && request.Content == null, "Catalog requests only read models");
                SentKey = request.Headers.Authorization != null && request.Headers.Authorization.ToString() == "Bearer fixture-key";
                Requests.Add(request.RequestUri);
                return Task.FromResult(Reply(Requests.Count));
            }
        }

        private static HttpResponseMessage Json(string body)
        { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }; }

        public static void Run()
        {
            Check(Translator.ModelsEndpoint("http://localhost:11434").AbsolutePath == "/v1/models", "Root catalog endpoint");
            Check(Translator.ModelsEndpoint("https://example.test/chat/completions/").AbsolutePath == "/models", "Explicit root chat route");
            Check(Translator.Endpoint("https://example.test/proxy/models").AbsolutePath == "/proxy/models/chat/completions" &&
                Translator.ModelsEndpoint("https://example.test/proxy/models").AbsolutePath == "/proxy/models/models", "Preserve a provider prefix ending in models");
            var handler = new Handler();
            using (var translator = new Translator(handler))
            {
                handler.Reply = n => n == 1 ? Json("{\"data\":[{\"id\":\"z/model\"},{\"id\":\"a:b\"}],\"has_more\":true,\"last_id\":\"a:b\"}") :
                    Json("{\"data\":[{\"id\":\"z/model\"},null,{\"id\":\"  \"},{\"id\":\"embed-v1\"}]}");
                var ids = translator.ListModelsAsync("https://example.test/proxy/v1/chat/completions/?api-version=test", "fixture-key", CancellationToken.None).GetAwaiter().GetResult();
                Check(ids.SequenceEqual(new[] { "a:b", "embed-v1", "z/model" }) && handler.SentKey, "All unique models and authentication");
                Check(handler.Requests.Count == 2 && handler.Requests.All(u => u.AbsolutePath == "/proxy/v1/models" && u.Query.Contains("api-version=test")) &&
                    Uri.UnescapeDataString(handler.Requests[1].Query).Contains("after=a:b"), "Pagination preserves route and query");
                handler.Reply = n => Json("{\"data\":[]}");
                Check(translator.ListModelsAsync("http://localhost:11434/v1", "", CancellationToken.None).Result.Length == 0 && !handler.SentKey, "Keyless and empty catalog");
                foreach (var body in new[] { "not json", "{\"unsupported\":true}", "{\"data\":[{\"id\":\"same\"}],\"has_more\":true}" })
                {
                    handler.Reply = n => Json(body);
                    bool rejected = false;
                    try { translator.ListModelsAsync("http://localhost/v1", "", CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (TranslationException) { rejected = true; }
                    Check(rejected, "Do not publish an invalid or incomplete catalog");
                }
            }
            string directory = Path.Combine(Path.GetTempPath(), "ShiyiSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "settings.json");
            try
            {
                File.WriteAllText(file, "{\"Model\":\"existing-model\",\"Configured\":true,\"SelectionButton\":false}");
                var store = new SettingsStore(directory);
                string note; var p = store.Read(out note);
                Check(note == null && p.Model == "existing-model" && p.Configured && !p.SelectionButton &&
                    p.PopupOpacity == 100 && p.AppearanceMode == "light" && p.Theme == "shiyi", "Legacy preferences");
                p.PopupOpacity = 67; p.AppearanceMode = "dark"; p.Theme = "iris"; p.AccentColor = ThemePreset.Hex("aBc"); p.ApiKey = "private-fixture-key";
                store.Save(p); var loaded = store.Read(out note);
                Check(loaded.PopupOpacity == 67 && loaded.Theme == "iris" && loaded.AppearanceMode == "dark" && loaded.AccentColor == "#AABBCC" &&
                    loaded.ApiKey == p.ApiKey && !File.ReadAllText(file).Contains(p.ApiKey), "Appearance and encrypted connection round trip");
                File.WriteAllText(file, "{\"PopupOpacity\":-3,\"Theme\":\"missing\",\"AppearanceMode\":\"bad\",\"AccentColor\":\"bogus\"}");
                loaded = store.Read(out note);
                Check(loaded.PopupOpacity == Preferences.MinOpacity && loaded.Theme == "shiyi" && loaded.AppearanceMode == "light" && loaded.AccentColor == "", "Invalid appearance values");
            }
            finally { if (File.Exists(file)) File.Delete(file); Directory.Delete(directory); }
            Console.WriteLine("Settings and model catalog checks passed.");
        }
    }
}
