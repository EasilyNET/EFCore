﻿using EasilyNET.AutoDependencyInjection;
using EasilyNET.Core.BaseType;
using EasilyNET.Core.Domains;
using EasilyNET.Core.Domains.Commands;
using EasilyNET.EntityFrameworkCore.Extensions;
using EasilyNET.EntityFrameworkCore.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;

namespace EasilyNET.EntityFrameworkCore.Test;

[TestClass]
public class RepositoryTests
{
    [TestMethod, Priority(1)]
    public async Task AddUserAsync_ShouldAddUserToDatabase()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        var userRepository = application.ServiceProvider!.GetRequiredService<IUserRepository>();
        for (var i = 0; i < 10; i++)
        {
            var user = new User($"大黄瓜_{i}", 18);
            await userRepository.AddAsync(user);
        }
        // Act
        var re = await userRepository.UnitOfWork.SaveChangesAsync();
        // Assert
        Assert.IsTrue(re > 0);
        // Arrange
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [TestMethod, Priority(10)]
    public async Task UpdateUserAsync_ShouldUpdateUserToDatabase()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        // Arrange
        var userRepository = application.ServiceProvider!.GetRequiredService<IUserRepository>();
        // Act
        var user = await userRepository.FindEntity.FirstAsync();
        Debug.WriteLine($"更新用户{user.Id}_{user.Name}");
        user?.ChangeName("大黄瓜_Test");
        userRepository.Update(user!);
        await userRepository.UnitOfWork.SaveChangesAsync();
        // Assert
        var newUser = await userRepository.FindAsync(user!.Id);
        Assert.IsTrue(newUser?.Equals(user));
    }

    /// <summary>
    /// </summary>
    /// <returns></returns>
    [TestMethod, Priority(20)]
    public async Task DeleteUserAsync_ShouldDeleteUserToDatabase()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        // Arrange
        var userRepository = application.ServiceProvider!.GetRequiredService<IUserRepository>();
        // Act
        var user = await userRepository.FindEntity.FirstOrDefaultAsync();
        Debug.WriteLine($"删除用户{user?.Id}_{user?.Name}");
        userRepository.Remove(user!);
        var count = await userRepository.UnitOfWork.SaveChangesAsync();
        // Assert
        Assert.IsTrue(count == 1);
    }

    /// <summary>
    /// 添加角色
    /// </summary>
    [TestMethod, Priority(4)]
    public async Task AddRoleAsync_ShouldAddRoleToDatabase()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        // Arrange
        var snowFlakeId = application.ServiceProvider!.GetService<ISnowFlakeId>();
        var roleRepository = application.ServiceProvider!.GetService<IRepository<Role, long>>();
        for (var i = 0; i < 10; i++)
        {
            var role = new Role(snowFlakeId!.NextId(), $"大黄瓜_{i}");
            await roleRepository!.AddAsync(role);
        }
        // Act
        var count = await roleRepository!.UnitOfWork.SaveChangesAsync();
        // Assert
        Assert.IsTrue(count > 0);
    }

    /// <summary>
    /// 命令添加用户
    /// </summary>
    [TestMethod, Priority(5)]
    public async Task AddUserAsync_ShouldCommand()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        var addUserCommand = new AddUserCommand(new("Command", 200));
        var sender = application.ServiceProvider?.GetService<ISender>();
        var count = await sender!.Send(addUserCommand);
        Assert.IsTrue(count > 0);
    }

    /// <summary>
    /// 查询用户
    /// </summary>
    [TestMethod, Priority(6)]
    public async Task UserListQuery_ShouldUserList()
    {
        using var application = ApplicationFactory.Create<TestAppModule>();
        var query = new UserListQuery();
        var sender = application.ServiceProvider?.GetService<IMediator>();
        var result = await sender!.Send(query);
        Assert.IsTrue(result.Count > 0);
    }
}

public sealed class User : AggregateRoot<long>, IMayHaveCreator<long?>, IHasCreationTime, IHasModifierId<long?>, IHasModificationTime, IHasDeleterId<long?>, IHasDeletionTime, IQuery<UserListQuery>, IHasRowVersion
{
    private User() { }

    public User(string name, int age)
    {
        Name = name;
        Age = age;
        AddDomainEvent(new AddUserDomainEvent(this));
    }

    public string Name { get; private set; } = default!;

    public int Age { get; }

    /// <inheritdoc />
    public DateTime CreationTime { get; set; }

    /// <inheritdoc />
    public long? DeleterId { get; set; }

    /// <inheritdoc />
    public DateTime? DeletionTime { get; set; }

    /// <inheritdoc />
    public DateTime? LastModificationTime { get; set; }

    /// <inheritdoc />
    public long? LastModifierId { get; set; }

    /// <summary>
    /// </summary>
    public byte[] Version { get; set; } = default!;

    /// <inheritdoc />
    public long? CreatorId { get; set; }

    public void ChangeName(string name)
    {
        Name = name;
    }
}

public sealed class Role : Entity<long>, IHasRowVersion
{
    private Role() { }

    public Role(long id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Name { get; set; } = default!;

    public byte[] Version { get; set; } = default!;
}

// ReSharper disable once PartialTypeWithSinglePart
public partial class Test : Entity<long>, IHasCreationTime, IHasModifierId<long?>, IMayHaveCreator<long?>, IHasDeleterId<long?>, IHasDeletionTime, IHasModificationTime;

public sealed class TestDbContext : DefaultDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options, IServiceProvider? serviceProvider)
        : base(options, serviceProvider)
    {
        Database.EnsureCreated();
    }

    private static string NextId => SnowFlakeId.Default.NextId().ToString();

    protected override void ApplyConfigurations(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    //只是测试时候使用
    /// <inheritdoc />
    protected override string GetUserId() => NextId;
}

public interface IUserRepository : IRepository<User, long> { }

/// <summary>
/// UserRepository
/// </summary>
/// <param name="dbContext"></param>
public class UserRepository(TestDbContext dbContext) : RepositoryBase<User, long, TestDbContext>(dbContext), IUserRepository { }

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd().UseSnowFlakeValueGenerator(); //新增时使用生成雪花ID
        builder.Property(o => o.Name).IsRequired().HasMaxLength(50);

        // builder.ConfigureByConvention();
        builder.ToTable("User");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).IsRequired().HasMaxLength(50);
        // builder.ConfigureByConvention();
        builder.ToTable("Role");
    }
}

internal sealed record AddUserDomainEvent(User User) : IDomainEvent;

internal sealed class AddUserDomainEventHandler : IDomainEventHandler<AddUserDomainEvent>
{
    /// <inheritdoc />
    public Task Handle(AddUserDomainEvent notification, CancellationToken cancellationToken)
    {
        Debug.WriteLine($"创建用户{notification.User.Id}_{notification.User.Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// 添加用户命令
/// </summary>
/// <remarks>
/// 添加
/// </remarks>
/// <param name="user"></param>
internal sealed class AddUserCommand(User user) : ICommand<int>
{
    public User User { get; } = user;
}

/// <summary>
/// </summary>
/// <param name="userRepository"></param>
internal sealed class AddUserCommandHandler(IUserRepository userRepository) : ICommandHandler<AddUserCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(AddUserCommand request, CancellationToken cancellationToken)
    {
        await userRepository.AddAsync(request.User, cancellationToken);
        var count = await userRepository.UnitOfWork.SaveChangesAsync(cancellationToken);
        return count;
    }
}

/// <summary>
/// </summary>
internal sealed class UserListQuery : IQuery<List<User>> { }

/// <summary>
/// </summary>
/// <param name="userRepository"></param>
internal sealed class UserListQueryHandler(IUserRepository userRepository) : IQueryHandler<UserListQuery, List<User>>
{
    /// <inheritdoc />
    public async Task<List<User>> Handle(UserListQuery request, CancellationToken cancellationToken)
    {
        Debug.WriteLine("Handle_下读取用户");
        return await userRepository.FindEntity.ToListAsync(cancellationToken);
    }
}
