using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace HybridModelBinding.Tests
{
    /// <summary>
    /// An in-memory MVC app using `.AddHybridModelBinder()` exactly as a consuming app would.
    /// </summary>
    public sealed class TestApp : IAsyncLifetime
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        };

        private WebApplication app;

        public HttpClient Client { get; private set; }

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });

            builder.WebHost.UseTestServer();
            builder.Services
                .AddControllersWithViews()
                .AddApplicationPart(typeof(TestApp).Assembly)
                .AddHybridModelBinder();

            app = builder.Build();

            // Requests carrying `X-Test-UserId` are signed in with a `UserId` claim, for `Source.Claim`.
            app.Use((context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-UserId", out var userId))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("UserId", userId.ToString()) }, "Test"));
                }

                return next();
            });
            app.UseRouting();
            app.MapControllers();

            await app.StartAsync();

            Client = app.GetTestClient();
        }

        public async Task DisposeAsync()
        {
            Client?.Dispose();

            if (app != null)
            {
                await app.DisposeAsync();
            }
        }

        public async Task<T> GetAsync<T>(string url, params (string Name, string Value)[] headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            foreach (var (name, value) in headers)
            {
                request.Headers.Add(name, value);
            }

            return await SendAsync<T>(request);
        }

        public Task<T> PostFormAsync<T>(string url, params (string Key, string Value)[] fields)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value))),
            };

            return SendAsync<T>(request);
        }

        public Task<T> PostJsonAsync<T>(string url, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body),
            };

            return SendAsync<T>(request);
        }

        public async Task<T> SendAsync<T>(HttpRequestMessage request)
        {
            using (request)
            using (var response = await Client.SendAsync(request))
            {
                var content = await response.Content.ReadAsStringAsync();

                Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {content}");

                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }
        }
    }

    [CollectionDefinition(nameof(TestApp))]
    public class TestAppCollection : ICollectionFixture<TestApp>
    { }
}
