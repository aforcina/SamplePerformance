using System.Threading;

namespace SamplePerformance
{
    public class UnitOfWork : IUnitOfWork
    {
        public UnitOfWork(IItemRepository itemRepository)
        {
            ItemRepository = itemRepository;
        }

        public IItemRepository ItemRepository { get; }

        public void Commit()
        {
            Thread.Sleep(400);
        }
    }
}
