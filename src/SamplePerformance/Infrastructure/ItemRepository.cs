using System.Threading;

namespace SamplePerformance
{
    public class ItemRepository : IItemRepository
    {
        public ItemResponse GetById(int id)
        {
            Thread.Sleep(500);
            return new ItemResponse { Id = id, Status = "Loaded" };
        }
    }
}
