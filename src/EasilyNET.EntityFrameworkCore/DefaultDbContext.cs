using EasilyNET.Core;
using EasilyNET.Core.Abstractions;
using EasilyNET.Core.Helpers;
using System.Data;

namespace EasilyNET.EntityFrameworkCore;

/// <summary>
/// 默认EF CORE上下文
/// </summary>
public abstract class DefaultDbContext : DbContext, IUnitOfWork
{
    /// <summary>
    /// 配置基本属性方法
    /// </summary>
    private static readonly MethodInfo? ConfigureBasePropertiesMethodInfo
        = typeof(DefaultDbContext)
            .GetMethod(nameof(ConfigureBaseProperties),
                BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 当前事务
    /// </summary>
    private IDbContextTransaction? _currentTransaction;

    /// <summary>
    /// 是否释放
    /// </summary>
    private bool _isDisposed;

    /// <summary>
    /// </summary>
    /// <param name="options"></param>
    /// <param name="serviceProvider"></param>
    protected DefaultDbContext(DbContextOptions options, IServiceProvider? serviceProvider) : base(options)
    {
        ServiceProvider = serviceProvider;
        Logger = serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger<DefaultDbContext>() ?? NullLogger<DefaultDbContext>.Instance;
        Mediator = serviceProvider?.GetService<IMediator>() ?? NullMediator.Instance;
        CurrentUser = serviceProvider?.GetService<ICurrentUser>() ?? NullCurrentUser.Instance;
    }

    /// <summary>
    /// 中介者发布事件
    /// </summary>
    protected IMediator Mediator { get; }

    /// <summary>
    /// 当前用户
    /// </summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>
    /// 服务提供者
    /// </summary>

    protected IServiceProvider? ServiceProvider { get; }

    private ILogger? Logger { get; }

    /// <summary>
    /// 是否激活事务
    /// </summary>
    public bool HasActiveTransaction => _currentTransaction is not null;

    /// <summary>
    /// 异步开启事务
    /// </summary>
    /// <param name="isolationLevel"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.Unspecified, CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            Logger?.LogDebug("开启事务");
            _currentTransaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 异步提交并清除当前事务
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (HasActiveTransaction)
        {
            Logger?.LogDebug("提交事务");
            await _currentTransaction?.CommitAsync(cancellationToken)!;
            _currentTransaction = default;
        }
    }

    /// <summary>
    /// 异步回滚事务
    /// </summary>
    /// <param name="cancellationToken"></param>
    public virtual async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (HasActiveTransaction)
        {
            Logger?.LogDebug("回滚事务");
            await _currentTransaction?.RollbackAsync(cancellationToken)!;
            _currentTransaction = default;
        }
    }

    /// <summary>
    /// 内存释放
    /// </summary>
    public override void Dispose()
    {
        Dispose(true);
        //告诉GC，不要调用析构函数
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 析构函数
    /// </summary>
    ~DefaultDbContext()
    {
        //不释放
        Dispose(false);
    }

    /// <summary>
    /// 释放对象所占用的非托管和托管资源。
    /// </summary>
    /// <param name="disposing">为 true 则释放托管资源和非托管资源；为 false 则仅释放非托管资源。</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _currentTransaction?.Dispose();
            _currentTransaction = default;
            base.Dispose();
        }
        _isDisposed = true;
    }

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        ConfigLogLogging(optionsBuilder);
    }

    /// <summary>
    /// 处理日志
    /// </summary>
    /// <param name="optionsBuilder"></param>
    protected virtual void ConfigLogLogging(DbContextOptionsBuilder optionsBuilder)
    {
#if DEBUG
        optionsBuilder.EnableDetailedErrors();
        optionsBuilder.EnableSensitiveDataLogging();
#endif
    }

    /// <summary>
    /// 保存更改操作
    /// </summary>
    /// <param name="acceptAllChangesOnSuccess"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await SaveChangesBeforeAsync(cancellationToken).ConfigureAwait(false);
        var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        ChangeTracker.AutoDetectChangesEnabled = true;
        Logger?.LogInformation("保存{count}条数据", count);
        await SaveChangesAfterAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <summary>
    /// 异步开始保存更改
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual async Task SaveChangesBeforeAsync(CancellationToken cancellationToken = default)
    {
        var entityEntries = ChangeTracker.Entries();
        foreach (var entityEntry in entityEntries)
        {
            switch (entityEntry.State)
            {
                case EntityState.Added:
                    AddBefore(entityEntry);
                    break;
                case EntityState.Modified:
                    UpdateBefore(entityEntry);
                    break;
                case EntityState.Deleted:
                    DeleteBefore(entityEntry);
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                default: continue;
            }
        }
        await DispatchSaveBeforeEventsAsync(cancellationToken);
    }

    /// <summary>
    /// 添加前操作
    /// </summary>
    /// <param name="entry"></param>
    protected virtual void AddBefore(EntityEntry entry)
    {
        SetCreatorAudited(entry);
        SetModifierAudited(entry);
        SetVersionAudited(entry);
    }

    /// <summary>
    /// 更新前删除
    /// </summary>
    /// <param name="entry"></param>
    protected virtual void UpdateBefore(EntityEntry entry)
    {
        SetModifierAudited(entry);
        SetVersionAudited(entry);
    }

    /// <summary>
    /// 删除前操作
    /// </summary>
    /// <param name="entry"></param>
    protected virtual void DeleteBefore(EntityEntry entry)
    {
        SetDeletedAudited(entry);
    }

    /// <summary>
    /// 异步结束保存更改
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    protected virtual Task SaveChangesAfterAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 设置创建者审计
    /// </summary>
    protected virtual void SetCreatorAudited(EntityEntry entry)
    {
        entry.SetCurrentValue(EFCoreShare.CreationTime, DateTime.Now);
        entry.SetPropertyValue(EFCoreShare.CreatorId, GetUserId());
    }

    /// <summary>
    /// 设置版本号
    /// </summary>
    /// <param name="entry"></param>
    protected virtual void SetVersionAudited(EntityEntry entry)
    {
        if (entry.Entity is IHasRowVersion { Version: null } version)
        {
            ObjectHelper.TrySetProperty(version, o => o.Version, () => GetRowVersion(entry.Entity));
            //version.Version = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
        }
    }

    /// <summary>
    /// 得到行版本号
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    protected virtual byte[]? GetRowVersion(object entity) => entity is IHasRowVersion ? Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")) : default;

    /// <summary>
    /// 设置修改审计
    /// </summary>
    protected virtual void SetModifierAudited(EntityEntry entry)
    {
        entry.SetPropertyValue(EFCoreShare.ModifierId, GetUserId());
        entry.SetCurrentValue(EFCoreShare.ModificationTime, DateTime.Now);
    }

    /// <summary>
    /// 设置删除
    /// </summary>
    protected virtual void SetDeletedAudited(EntityEntry entry)
    {
        entry.SetCurrentValue(EFCoreShare.IsDeleted, true);
        entry.SetCurrentValue(EFCoreShare.DeletionTime, DateTime.Now);
        entry.SetPropertyValue(EFCoreShare.DeleterId, GetUserId());
        entry.State = EntityState.Modified;
    }

    /// <summary>
    /// 配置模型
    /// </summary>
    /// <param name="modelBuilder"></param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ApplyConfigurations(modelBuilder);
        base.OnModelCreating(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ConfigureBasePropertiesMethodInfo?.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder, entityType]);
            ApplyRowVersion(modelBuilder, entityType);
        }
    }

    /// <summary>
    /// 配置实体类型
    /// </summary>
    /// <param name="modelBuilder"></param>
    protected virtual void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }

    /// <summary>
    /// 根据数据库类型配置版本号
    /// </summary>
    /// <param name="modelBuilder"></param>
    /// <param name="mutableEntityType"></param>
    protected virtual void ApplyRowVersion(ModelBuilder modelBuilder, IMutableEntityType mutableEntityType)
    {
        if (!mutableEntityType.ClrType.IsDeriveClassFrom<IHasRowVersion>()) return;
        modelBuilder.Entity(mutableEntityType.ClrType).Property<byte[]>(EFCoreShare.Version).IsRequired().HasComment("版本号").IsConcurrencyToken();
    }

    /// <summary>
    /// 配置基本属性
    /// </summary>
    /// <param name="modelBuilder"></param>
    /// <param name="mutableEntityType"></param>
    /// <typeparam name="TEntity"></typeparam>
    protected virtual void ConfigureBaseProperties<TEntity>(ModelBuilder modelBuilder, IMutableEntityType mutableEntityType)
        where TEntity : class
    {
        if (mutableEntityType.IsOwned())
        {
            return;
        }
        if (!typeof(IEntity).IsAssignableFrom(typeof(TEntity)))
        {
            return;
        }
        modelBuilder.Entity<TEntity>().ConfigureByConvention();
        modelBuilder.Entity<TEntity>().ConfigureSoftDelete();
    }

    /// <summary>
    /// 异步调度发生前事件
    /// </summary>
    protected virtual async Task DispatchSaveBeforeEventsAsync(CancellationToken cancellationToken = default)
    {
        await Mediator.DispatchDomainEventsAsync(this, cancellationToken);
    }

    /// <summary>
    /// 得到当前用户
    /// </summary>
    /// <returns></returns>
    protected virtual string? GetUserId() => CurrentUser.GetUserId<string>();
}