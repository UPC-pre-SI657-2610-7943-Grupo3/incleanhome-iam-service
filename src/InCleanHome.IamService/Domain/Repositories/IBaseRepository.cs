namespace InCleanHome.IamService.Domain.Repositories;

/// <summary>Generic repository contract used by every aggregate-root repository.</summary>
public interface IBaseRepository<TEntity>
{
    Task AddAsync(TEntity entity);
    Task<TEntity?> FindByIdAsync(int id);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<IEnumerable<TEntity>> ListAsync();
}

/// <summary>Unit of Work — commits all pending changes in a single transaction.</summary>
public interface IUnitOfWork
{
    Task CompleteAsync();
}
