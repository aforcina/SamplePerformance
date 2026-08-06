using System.Net.Http;
using System.Threading;

namespace SamplePerformance
{
    public class ExternalItemGateway : IExternalItemGateway
    {
        private readonly HttpClient _httpClient;

        public ExternalItemGateway(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public void Send(ItemRequest request)
        {
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://example.com/api/notify"))
            {
                requestMessage.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                _httpClient.SendAsync(requestMessage).GetAwaiter().GetResult();
            }

            Thread.Sleep(900);
        }
    }
}
