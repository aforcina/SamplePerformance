using System.Web.Http;

namespace SamplePerformance
{
    public class HealthController : ApiController
    {
        public IHttpActionResult Get()
        {
            return Ok(new { status = "Healthy" });
        }
    }
}
