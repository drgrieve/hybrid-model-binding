using Xunit;

namespace HybridModelBinding.Tests
{
    [Collection(nameof(TestApp))]
    public class NestedBindingTests
    {
        private readonly TestApp app;

        public NestedBindingTests(TestApp app)
        {
            this.app = app;
        }

        [Fact]
        public async Task Nested_object_binds_from_query_string()
        {
            var model = await app.GetAsync<NestedModel>("/test/nested?address.street=1%20Main%20St&address.city=Sydney");

            Assert.Equal("1 Main St", model.Address?.Street);
            Assert.Equal("Sydney", model.Address?.City);
            Assert.Equal(Source.QueryString, model.HybridBoundProperties["Address"]);
        }

        [Fact]
        public async Task Nested_object_binds_from_form()
        {
            var model = await app.PostFormAsync<NestedModel>(
                "/test/nested",
                ("Name", "Form"),
                ("Address.Street", "2 High St"),
                ("Address.City", "Perth"));

            Assert.Equal("Form", model.Name);
            Assert.Equal("2 High St", model.Address?.Street);
            Assert.Equal("Perth", model.Address?.City);
            Assert.Equal(Source.Form, model.HybridBoundProperties["Address"]);
        }

        [Fact]
        public async Task Nested_object_binds_from_jquery_form_keys()
        {
            var model = await app.PostFormAsync<NestedModel>(
                "/test/nested",
                ("address[street]", "3 Low St"),
                ("address[city]", "Hobart"));

            Assert.Equal("3 Low St", model.Address?.Street);
            Assert.Equal("Hobart", model.Address?.City);
        }

        [Fact]
        public async Task List_of_objects_binds_from_query_string_with_dot_keys()
        {
            var model = await app.GetAsync<NestedModel>(
                "/test/nested?changes[0].description=First&changes[0].amount=1.5&changes[1].description=Second&changes[1].amount=2");

            Assert.NotNull(model.Changes);
            Assert.Collection(
                model.Changes,
                x => { Assert.Equal("First", x.Description); Assert.Equal(1.5m, x.Amount); },
                x => { Assert.Equal("Second", x.Description); Assert.Equal(2m, x.Amount); });
        }

        [Fact]
        public async Task List_of_objects_binds_from_query_string_with_jquery_bracket_keys()
        {
            // What jQuery's $.param produces for { changes: [{ description: 'First' }, { description: 'Second' }] }.
            var model = await app.GetAsync<NestedModel>(
                "/test/nested?changes%5B0%5D%5Bdescription%5D=First&changes%5B1%5D%5Bdescription%5D=Second&changes%5B1%5D%5Bamount%5D=3");

            Assert.NotNull(model.Changes);
            Assert.Collection(
                model.Changes,
                x => Assert.Equal("First", x.Description),
                x => { Assert.Equal("Second", x.Description); Assert.Equal(3m, x.Amount); });
        }

        [Fact]
        public async Task List_of_objects_binds_from_form_with_dot_and_jquery_keys()
        {
            var dotted = await app.PostFormAsync<NestedModel>(
                "/test/nested",
                ("Changes[0].Description", "First"),
                ("Changes[1].Description", "Second"));

            var jquery = await app.PostFormAsync<NestedModel>(
                "/test/nested",
                ("changes[0][description]", "First"),
                ("changes[1][description]", "Second"));

            Assert.Equal(new[] { "First", "Second" }, dotted.Changes?.Select(x => x.Description));
            Assert.Equal(new[] { "First", "Second" }, jquery.Changes?.Select(x => x.Description));
        }

        [Fact]
        public async Task Get_only_collection_is_filled_in_place()
        {
            var model = await app.GetAsync<NestedModel>("/test/nested?readOnlyChanges[0].description=Kept");

            Assert.Equal(new[] { "Kept" }, model.ReadOnlyChanges.Select(x => x.Description));
        }

        [Fact]
        public async Task Scalar_lists_bind_from_repeated_keys()
        {
            var model = await app.GetAsync<NestedModel>("/test/nested?ids=1&ids=2&tags=a&tags=b&numbers=4&numbers=5&labels=x&labels=x&labels=y");

            Assert.Equal(new[] { 1, 2 }, model.Ids);
            Assert.Equal(new[] { "a", "b" }, model.Tags);
            Assert.Equal(new[] { 4, 5 }, model.Numbers);
            Assert.Equal(new[] { "x", "y" }, model.Labels?.OrderBy(x => x));
        }

        [Fact]
        public async Task Scalar_lists_bind_from_jquery_bracket_keys()
        {
            var model = await app.GetAsync<NestedModel>("/test/nested?ids%5B%5D=1&ids%5B%5D=2&numbers%5B%5D=7");

            Assert.Equal(new[] { 1, 2 }, model.Ids);
            Assert.Equal(new[] { 7 }, model.Numbers);
        }

        [Fact]
        public async Task Scalar_lists_bind_from_form()
        {
            var model = await app.PostFormAsync<NestedModel>(
                "/test/nested",
                ("ids", "3"),
                ("ids", "4"),
                ("tags[]", "x"),
                ("tags[]", "y"));

            Assert.Equal(new[] { 3, 4 }, model.Ids);
            Assert.Equal(new[] { "x", "y" }, model.Tags);
        }

        [Fact]
        public async Task Jquery_bulk_email_query_binds_enum_and_id_list_without_attributes()
        {
            // AC-4241: `?reason=TopUp&practiceIds[]=1&practiceIds[]=2` needed [FromQuery] before.
            var model = await app.GetAsync<BulkEmailModel>("/test/bulk-email?reason=TopUp&practiceIds%5B%5D=1&practiceIds%5B%5D=2");

            Assert.Equal(Reason.TopUp, model.Reason);
            Assert.Equal(new[] { 1, 2 }, model.PracticeIds);
        }

        [Fact]
        public async Task Jquery_bulk_email_form_binds_enum_and_id_list_without_attributes()
        {
            var model = await app.PostFormAsync<BulkEmailModel>(
                "/test/bulk-email",
                ("reason", "Renewal"),
                ("practiceIds[]", "5"),
                ("practiceIds[]", "6"));

            Assert.Equal(Reason.Renewal, model.Reason);
            Assert.Equal(new[] { 5, 6 }, model.PracticeIds);
        }

        [Fact]
        public async Task Simple_properties_still_bind_alongside_nested_ones()
        {
            var model = await app.GetAsync<NestedModel>(
                "/test/nested/12?name=Mixed&reason=Renewal&date=2026-11-23&address.city=Darwin&changes[0][description]=One");

            Assert.Equal(12, model.Id);
            Assert.Equal("Mixed", model.Name);
            Assert.Equal(Reason.Renewal, model.Reason);
            Assert.Equal(new DateTime(2026, 11, 23), model.Date);
            Assert.Equal("Darwin", model.Address?.City);
            Assert.Equal("One", Assert.Single(model.Changes).Description);
        }

        [Fact]
        public async Task Properties_absent_from_the_request_stay_null()
        {
            var model = await app.GetAsync<NestedModel>("/test/nested?name=Only");

            Assert.Equal("Only", model.Name);
            Assert.Null(model.Address);
            Assert.Null(model.Changes);
            Assert.Null(model.Ids);
            Assert.False(model.HybridBoundProperties.ContainsKey("Address"));
        }

        [Fact]
        public async Task Form_wins_over_query_for_nested_properties_by_default()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/test/nested?address.city=Query")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["address.city"] = "Form" }),
            };

            var model = await app.SendAsync<NestedModel>(request);

            Assert.Equal("Form", model.Address?.City);
            Assert.Equal(Source.Form, model.HybridBoundProperties["Address"]);
        }

        [Fact]
        public async Task Nested_properties_respect_class_and_parameter_binding_order()
        {
            var queryFirst = await app.GetAsync<QueryFirstModel>("/test/query-first/1?id=2&address.city=Query");
            var routeOnly = await app.GetAsync<QueryFirstModel>("/test/from-hybrid/1?id=2&address.city=Query");

            Assert.Equal(2, queryFirst.Id);
            Assert.Equal("Query", queryFirst.Address?.City);

            // [FromHybrid(new[] { Source.Route })] excludes the query string for every property, nested ones included.
            Assert.Equal(1, routeOnly.Id);
            Assert.Null(routeOnly.Address);
        }

        [Fact]
        public async Task Invalid_nested_values_are_reported_in_model_state()
        {
            var result = await app.GetAsync<ModelStateResult>("/test/nested-state?changes[0].description=Bad&changes[0].amount=abc");

            Assert.False(result.IsValid);
            Assert.Contains("Changes[0].Amount", result.ErrorKeys);
            Assert.Equal("Bad", Assert.Single(result.Model.Changes).Description);
        }
    }
}
