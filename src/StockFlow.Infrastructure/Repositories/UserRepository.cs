using Dapper;
using StockFlow.Application.Interfaces;
using StockFlow.Domain.Entities;
using StockFlow.Infrastructure.Data;

namespace StockFlow.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly DbConnectionFactory _connectionFactory;
    public UserRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        const string sql = @"
            SELECT
                u.user_id AS UserId,
                u.role_id AS RoleId,
                r.role_name AS RoleName,
                r.is_active AS RoleIsActive,
                u.full_name AS FullName,
                u.username AS Username,
                u.password_hash AS PasswordHash,
                u.mobile_number AS MobileNumber,
                u.email AS Email,
                u.is_active AS IsActive,
                u.created_at AS CreatedAt
            FROM users u
            INNER JOIN roles r
                ON r.role_id = u.role_id
            WHERE u.username = @Username
            LIMIT 1;
        ";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(sql, new
        {
            Username = username
        });
    }

    public async Task<User?> GetByIdAsync(int userId)
    {
        const string sql = @"
            SELECT
                u.user_id AS UserId,
                u.role_id AS RoleId,
                r.role_name AS RoleName,
                r.is_active AS RoleIsActive,
                u.full_name AS FullName,
                u.username AS Username,
                u.password_hash AS PasswordHash,
                u.mobile_number AS MobileNumber,
                u.email AS Email,
                u.is_active AS IsActive,
                u.created_at AS CreatedAt
            FROM users u
            INNER JOIN roles r
                ON r.role_id = u.role_id
            WHERE u.user_id = @UserId;
        ";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<User>(sql, new
        {
            UserId = userId
        });
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        const string sql = @"
            SELECT
                u.user_id AS UserId,
                u.role_id AS RoleId,
                r.role_name AS RoleName,
                r.is_active AS RoleIsActive,
                u.full_name AS FullName,
                u.username AS Username,
                u.mobile_number AS MobileNumber,
                u.email AS Email,
                u.is_active AS IsActive,
                u.created_at AS CreatedAt
            FROM users u
            INNER JOIN roles r
                ON r.role_id = u.role_id
            ORDER BY u.full_name;
        ";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.QueryAsync<User>(sql);
    }

    public async Task<int> CreateAsync(User user)
    {
        const string sql = @"
            INSERT INTO users
            (
                role_id,
                full_name,
                username,
                password_hash,
                mobile_number,
                email,
                is_active
            )
            VALUES
            (
                @RoleId,
                @FullName,
                @Username,
                @PasswordHash,
                @MobileNumber,
                @Email,
                @IsActive
            )
            RETURNING user_id;
        ";
        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(sql, user);
    }

    public async Task<bool> UpdateAsync(User user)
    {
        const string sql = @"
            UPDATE users
            SET
                role_id = @RoleId,
                full_name = @FullName,
                mobile_number = @MobileNumber,
                email = @Email,
                is_active = @IsActive,
                updated_at = CURRENT_TIMESTAMP
            WHERE user_id = @UserId;
        ";
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(sql, user);
        return rows > 0;
    }
}
