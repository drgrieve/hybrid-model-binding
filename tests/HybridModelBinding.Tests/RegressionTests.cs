using System.Net.Http.Headers;
using Xunit;

namespace HybridModelBinding.Tests
{
    /// <summary>
    /// Behaviour the by-name pass already had, which the nested-binding change must keep.
    /// </summary>
    [Collection(nameof(TestApp))]
    public class RegressionTests
    {
        private readonly TestApp app;

        public RegressionTests(TestApp app)
        {
            this.app = app;
        }

        [Fact]
        public async Task Json_body_is_merged_with_route_id()
        {
            var model = await app.PostJsonAsync<NestedModel>("/test/nested/42", new
            {
                name = "Body",
                address = new { street = "4 Body St", city = "Cairns" },
                changes = new[] { new { description = "From body", amount = 9.5m } },
            });

            Assert.Equal(42, model.Id);
            Assert.Equal(Source.Route, model.HybridBoundProperties["Id"]);
            Assert.Equal("Body", model.Name);
            Assert.Equal(Source.Body, model.HybridBoundProperties["Name"]);
            Assert.Equal("Cairns", model.Address?.City);
            Assert.Equal(Source.Body, model.HybridBoundProperties["Address"]);
            Assert.Equal("From body", Assert.Single(model.Changes).Description);
            Assert.Equal(Source.Body, model.HybridBoundProperties["Changes"]);
        }

        [Fact]
        public async Task Json_body_wins_over_route_and_query_by_default()
        {
            var model = await app.PostJsonAsync<NestedModel>("/test/nested/42?name=Query&address.city=Query", new
            {
                id = 7,
                name = "Body",
                address = new { city = "Body" },
            });

            Assert.Equal(7, model.Id);
            Assert.Equal("Body", model.Name);
            Assert.Equal("Body", model.Address?.City);
        }

        [Fact]
        public async Task Json_body_arrays_bind_to_array_properties()
        {
            var model = await app.PostJsonAsync<NestedModel>("/test/nested", new { numbers = new[] { 1, 2, 3 }, ids = new[] { 4 } });

            Assert.Equal(new[] { 1, 2, 3 }, model.Numbers);
            Assert.Equal(new[] { 4 }, model.Ids);
        }

        [Fact]
        public async Task Route_wins_over_query_with_fallback_order()
        {
            var model = await app.GetAsync<OrderingModel>("/test/ordering/5/Route?id=6&name=Query");

            Assert.Equal(5, model.Id);
            Assert.Equal(Source.Route, model.HybridBoundProperties["Id"]);

            // `Name` lists Body, Form, QueryString, Route, so the query string wins for it.
            Assert.Equal("Query", model.Name);
            Assert.Equal(Source.QueryString, model.HybridBoundProperties["Name"]);
        }

        [Fact]
        public async Task Header_attribute_with_alternate_name_wins_when_declared_first()
        {
            var model = await app.GetAsync<OrderingModel>("/test/ordering/5/Route?name=Query", ("X-Name", "Header"));

            Assert.Equal("Header", model.Name);
            Assert.Equal(Source.Header, model.HybridBoundProperties["Name"]);
        }

        [Fact]
        public async Task Form_value_binds_when_no_header()
        {
            var model = await app.PostFormAsync<OrderingModel>("/test/ordering/5", ("name", "Form"), ("id", "9"));

            Assert.Equal(9, model.Id);
            Assert.Equal("Form", model.Name);
            Assert.Equal(Source.Form, model.HybridBoundProperties["Name"]);
        }

        [Fact]
        public async Task Claim_binds()
        {
            var model = await app.GetAsync<OrderingModel>("/test/ordering", ("X-Test-UserId", "user-1"));

            Assert.Equal("user-1", model.UserId);
            Assert.Equal(Source.Claim, model.HybridBoundProperties["UserId"]);
        }

        [Fact]
        public async Task Class_attribute_and_parameter_attribute_override_fallback_order()
        {
            var classOrder = await app.GetAsync<QueryFirstModel>("/test/query-first/1?id=2");
            var parameterOrder = await app.GetAsync<QueryFirstModel>("/test/from-hybrid/1?id=2");

            Assert.Equal(2, classOrder.Id);
            Assert.Equal(Source.QueryString, classOrder.HybridBoundProperties["Id"]);
            Assert.Equal(1, parameterOrder.Id);
            Assert.Equal(Source.Route, parameterOrder.HybridBoundProperties["Id"]);
        }

        [Fact]
        public async Task Files_bind_from_multipart_form()
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent("Uploader"), "Name" },
                { File("one.txt"), "File", "one.txt" },
                { File("a.txt"), "Files", "a.txt" },
                { File("b.txt"), "Files", "b.txt" },
            };

            var result = await app.SendAsync<UploadResult>(new HttpRequestMessage(HttpMethod.Post, "/test/upload") { Content = content });

            Assert.Equal("Uploader", result.Name);
            Assert.Equal("one.txt", result.FileName);
            Assert.Equal(new[] { "a.txt", "b.txt" }, result.FileNames);
        }

        private static ByteArrayContent File(string name)
        {
            var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(name));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

            return file;
        }
    }
}
