# HybridModelBinding2

*For those who want the utmost flexibility in model binding.*

The hybrid approach differs from traditional model binding since you get to work with both `IModelBinder` and `IValueProvider`. This means your model can first get bound with data from the body of a request, and then get updated with data from route or querystring-attributes (in that order). This has the most benefit for users who prefer to have one-model for their request representations.

## A maintained fork

`HybridModelBinding2` is a maintained fork of [billbogaiv/hybrid-model-binding](https://github.com/billbogaiv/hybrid-model-binding) (the `HybridModelBinding` package, last released in 2022). The API, namespaces and attributes are unchanged, so switching is a package swap. What this fork adds:

- **Current ASP.NET Core.** Targets `net8.0` and `net10.0` through the shared framework (`Microsoft.AspNetCore.App`) instead of `netcoreapp3.1`/`netstandard2.1`, so it no longer pulls in the old `Microsoft.AspNetCore.Mvc.Core 2.1.x` and `Microsoft.AspNetCore.Http 2.1.x` packages.
- **Nested objects and lists of objects bind.** The original binder only matched top-level property names, so a `List<Change>` or an `Address` property was silently left empty unless the request was JSON. Properties the by-name pass cannot fill are now handed to MVC's own model binders, using the same source ordering (see [Nested objects and collections](#nested-objects-and-collections)).
- **jQuery-style keys.** `changes[0][description]=...` and `ids[]=1&ids[]=2`, as produced by `$.ajax`/`$.param`, bind from the query string and form without `[FromQuery]`/`[FromForm]` on the action.
- **Array and set properties** (`int[]`, `HashSet<T>`) bind instead of throwing.
- Tests, a modern CI build, MIT license expression and SourceLink symbols.

## Install

```shell
dotnet add package HybridModelBinding2
```

If you used the original package, remove `HybridModelBinding` and add `HybridModelBinding2`; no code changes are needed.

## Examples

### ASP.NET Core (net8.0 and later)

#### Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllersWithViews()

    /**
     * This will also register a MVC-convention to auto-apply hybrid-binding behavior when a controller-action has a single parameter.
     * There are additional rules that get enforced to make sure the class is instantiable before applying this convention.
     */
    .AddHybridModelBinder(options =>
    {
        /**
         * This is optional and overrides internal ordering of how binding gets applied to a model that doesn't have explicit binding-rules.
         * Internal ordering is: body => form-values => route-values => querystring-values => header-values => claims
         */
        options.FallbackBindingOrder = new[] { Source.QueryString, Source.Route };
    });
```

#### Model

```csharp
using HybridModelBinding;

[HybridBindClass(defaultBindingOrder: new[] { Source.Header, Source.Body, Source.Form, Source.QueryString, Source.Route })]
public class IndexModel
{
    // Binding will result from ordering specified at the class-level.
    public int? Age { get; set; }

    /// <summary>
    /// For this specific source, we want to bind to a header-key of `X-Name`.
    /// Additionally, this has higher-precedence when looking for a binding-source.
    /// </summary>
    [HybridBindProperty(Source.Header, "X-Name")]

    /// <summary>
    /// These are bound using the source-key of `Name`.
    /// </summary>
    [HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route })]
    public string Name { get; set; }
}
```

##### HybridBindClass

This attribute is optional. It allows specifying default-ordering when a property is not decorated with `HybridBindProperty`. This will override `FallbackBindingOrder`.

##### HybridBindProperty

This attribute is optional. It will override `HybridBindClass` and `FallbackBindingOrder`. It also allows specifying order. This is implicitly set based on the line-number of the attribute. It can also be explicitly set to avoid confusion:

```csharp
[HybridBindProperty(Source.Header, "X-Name", order: 5)]
[HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route }, order: 10)]
public string Name { get; set; }
```

Declaring multiple attributes on the same line may cause unpredictable results and should be avoided (unless using explicit ordering):

```csharp
[HybridBindProperty(Source.Header, "X-Name")][HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route })]
public string Name { get; set; }
```

This way is also valid, but may look odd depending whether you read lists top-bottom or bottom-top. In this case, the `Source.Body...`-attribute is given higher priority.

```csharp
[HybridBindProperty(Source.Header, "X-Name", order: 10)]
[HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route }, order: 5)]
public string Name { get; set; }
```

##### IHybridBoundModel

Maybe you want to see how a property received its value. If your model implements `IHybridBoundModel`, this will give you what you seek. This is optional and not required for everything else to work.

```csharp
public class IndexModel : IHybridBoundModel
{
    public int? Age { get; set; }

    [HybridBindProperty(Source.Header, "X-Name")]
    [HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route })]
    public string Name { get; set; }

    /**
     * Key is the property-name
     * Value is the source of the binding
     */
    public IDictionary<string, string> HybridBoundProperties { get; } = new Dictionary<string, string>();
}
```

#### Controller

```csharp
[HttpGet]
[Route("{age?}/{name?}")]
public IActionResult Index(IndexModel model)
{ }

/**
 * This action needs to declare `[FromHybrid]` since the registered convention won't hook it up to the library.
 * Without the parameter-attribute, default .NET behavior will get applied—even if your model is decorated with hybrid-attributes.
 */
[HttpGet]
[Route("{age?}/{name?}")]
public IActionResult IndexAlternate(int age, [FromHybrid]IndexModel model)
{ }
```

##### FromHybrid

*This attribute is optional if the action only contains one, class-parameter*. When used, however, also has the ability to specify default-ordering when a property is not decorated with `HybridBindProperty`. This will override `FallbackBindingOrder` and `HybridBindClass`. This is useful if an action has different requirements than other actions using the same model:

```csharp
[HttpGet]
[Route("{age?}/{name?}")]
public IActionResult IndexAlternate([FromHybrid(new[] { Source.QueryString, Source.Route })]IndexModel model)
{ }
```

#### View

```html
<h3>Age: @(Model.Age.HasValue ? Model.Age.ToString() : "N/A")</h3>
<h3>Name: @(string.IsNullOrEmpty(Model.Name) ? "N/A" : Model.Name)</h3>
```

## Results

Based on the setup above, here is how various URIs will get parsed/rendered:

### /

```html
<h3>Age: N/A</h3>
<h3>Name: N/A</h3>
```

### /10

```html
<h3>Age: 10</h3>
<h3>Name: N/A</h3>
```

### /10/Bill

```html
<h3>Age: 10</h3>
<h3>Name: Bill</h3>
```

### /10/Bill?age=20

```html
<h3>Age: 20</h3>
<h3>Name: Bill</h3>
```

### /10/Bill?age=20&name=Boga

```html
<h3>Age: 20</h3>
<h3>Name: Boga</h3> <!--`Name` lists QueryString before Route, so the querystring wins-->
```

### Example using headers and alternate-naming

```shell
curl -X GET \
  http://localhost/10/Bill?name=Boga \
  -H 'X-Name: Billy'
```

```html
<h3>Age: 10</h3>
<h3>Name: Billy</h3>
```

## Nested objects and collections

Properties are first bound by name, as above: strings, numbers, enums, dates, files and lists of those, plus anything present in a JSON body. Any property still unbound after that pass whose type is a class or a collection (`Address`, `List<Change>`, `int[]`, `HashSet<string>`) is then bound by MVC's own model binders, provided the request has data for it:

- The property's sources are tried in the same order as for simple properties (`FromHybrid`, then `HybridBindClass`, then `FallbackBindingOrder`, or the property's `HybridBindProperty` sources). The first source with data for the property wins.
- Only the form, query string and route are used for this. The body (JSON) is already bound as a whole, and headers and claims hold single values.
- Both MVC key styles and jQuery's style are understood, for the form and the query string:

| Request | Binds |
| --- | --- |
| `address.street=1+Main+St&address.city=Sydney` | `Address Address` |
| `changes[0].description=A&changes[1].description=B` | `List<Change> Changes` |
| `changes[0][description]=A&changes[1][description]=B` (jQuery) | `List<Change> Changes` |
| `ids=1&ids=2` or `ids[]=1&ids[]=2` (jQuery) | `List<int> Ids`, `int[] Ids` |

- Validation errors from nested values land in `ModelState` under their full key (`Changes[0].Amount`), as with MVC's default binding.
- If the model implements `IHybridBoundModel`, a nested property is recorded against the source it came from.

```csharp
public class EditEndorsementModel
{
    public int Id { get; set; }                          // route
    public string Reason { get; set; }                   // query or form
    public List<EndorsementChange> Changes { get; set; } // changes[0][description]=... from $.ajax
}

[HttpGet("{id}")]
public IActionResult EditEndorsement(EditEndorsementModel model) // no [FromQuery] needed
{ }
```

## Versioning and releases

Versions come from git tags via [MinVer](https://github.com/adamralph/minver): tag `v1.2.3` builds `1.2.3`; commits after a tag build `1.2.4-alpha.0.N`. CI builds and tests every push and pull request, and packs and pushes to NuGet only for `v*` tags, using NuGet trusted publishing (no stored API key).

## License

MIT. Original work by Bill Boga; fork maintained by David Grieve.
