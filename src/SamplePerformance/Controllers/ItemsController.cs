using System.Threading.Tasks;
using System.Web.Http;

namespace SamplePerformance
{
    public class ItemsController : ApiController
    {
        private readonly IItemProcessingService _itemProcessingService;

        public ItemsController(IItemProcessingService itemProcessingService)
        {
            _itemProcessingService = itemProcessingService;
        }

        public async Task<IHttpActionResult> Post(ItemRequest request)
        {
            var response = await _itemProcessingService.ProcessAsync(request);
            return Ok(response);
        }
    }
}
