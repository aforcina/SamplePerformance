namespace SamplePerformance
{
    public interface IUnitOfWork
    {
        IItemRepository ItemRepository { get; }
        void Commit();
    }
}
