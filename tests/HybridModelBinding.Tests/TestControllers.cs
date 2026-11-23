using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HybridModelBinding.Tests
{
    public enum Reason
    {
        None,
        TopUp,
        Renewal,
    }

    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
    }

    public class Change
    {
        public string Description { get; set; }
        public decimal Amount { get; set; }
    }

    public class NestedModel : IHybridBoundModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Reason Reason { get; set; }
        public DateTime? Date { get; set; }
        public Address Address { get; set; }
        public List<Change> Changes { get; set; }
        public List<int> Ids { get; set; }
        public List<string> Tags { get; set; }
        public int[] Numbers { get; set; }
        public HashSet<string> Labels { get; set; }
        public IList<Change> ReadOnlyChanges { get; } = new List<Change>();

        public IDictionary<string, string> HybridBoundProperties { get; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// What the `[FromQuery]`/`[FromForm]` workarounds exist for: a scalar list and an enum sent by jQuery.
    /// </summary>
    public class BulkEmailModel
    {
        public Reason Reason { get; set; }
        public List<int> PracticeIds { get; set; }
    }

    public class OrderingModel : IHybridBoundModel
    {
        public int Id { get; set; }

        [HybridBindProperty(Source.Header, "X-Name")]
        [HybridBindProperty(new[] { Source.Body, Source.Form, Source.QueryString, Source.Route })]
        public string Name { get; set; }

        [HybridBindProperty(Source.Claim)]
        public string UserId { get; set; }

        public IDictionary<string, string> HybridBoundProperties { get; } = new Dictionary<string, string>();
    }

    [HybridBindClass(defaultBindingOrder: new[] { Source.QueryString, Source.Route })]
    public class QueryFirstModel : IHybridBoundModel
    {
        public int Id { get; set; }
        public Address Address { get; set; }

        public IDictionary<string, string> HybridBoundProperties { get; } = new Dictionary<string, string>();
    }


    public class UploadModel
    {
        public string Name { get; set; }
        public IFormFile File { get; set; }
        public List<IFormFile> Files { get; set; }
    }

    public class UploadResult
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public List<string> FileNames { get; set; }
    }

    public class ModelStateResult
    {
        public bool IsValid { get; set; }
        public List<string> ErrorKeys { get; set; }
        public NestedModel Model { get; set; }
    }

    [Route("test")]
    public class TestController : Controller
    {
        [HttpGet("nested/{id?}")]
        [HttpPost("nested/{id?}")]
        public IActionResult Nested(NestedModel model) => Json(model);

        [HttpGet("nested-state")]
        [HttpPost("nested-state")]
        public IActionResult NestedState(NestedModel model) => Json(new ModelStateResult
        {
            IsValid = ModelState.IsValid,
            ErrorKeys = ModelState.Where(x => x.Value.Errors.Any()).Select(x => x.Key).ToList(),
            Model = model,
        });

        [HttpGet("bulk-email")]
        [HttpPost("bulk-email")]
        public IActionResult BulkEmail(BulkEmailModel model) => Json(model);

        [HttpGet("ordering/{id?}/{name?}")]
        [HttpPost("ordering/{id?}/{name?}")]
        public IActionResult Ordering(OrderingModel model) => Json(model);

        [HttpGet("query-first/{id?}")]
        public IActionResult QueryFirst(QueryFirstModel model) => Json(model);

        [HttpGet("from-hybrid/{id?}")]
        public IActionResult FromHybrid([FromHybrid(new[] { Source.Route })] QueryFirstModel model) => Json(model);

        [HttpPost("upload")]
        public IActionResult Upload(UploadModel model) => Json(new UploadResult
        {
            Name = model.Name,
            FileName = model.File?.FileName,
            FileNames = model.Files?.Select(x => x.FileName).ToList(),
        });
    }
}
