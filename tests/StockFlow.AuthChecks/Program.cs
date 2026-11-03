using Microsoft.AspNetCore.Identity;
using StockFlow.Application.DTOs.Auth;
using StockFlow.Application.Interfaces;
using StockFlow.Application.Services;
using StockFlow.Domain.Entities;

var repository = new FakeUsers();
var auth = new AuthService(repository);
var request = new LoginRequest { Username = "admin", Password = "Test-only-password-42" };
var unknown = await auth.ValidateLoginAsync(request);
Check(!unknown.Success, "unknown user rejected");
var user = new User { UserId = 7, Username = "admin", FullName = "Test Owner", IsActive = true, RoleIsActive = true };
await new UserService(repository).CreateUserAsync(user, request.Password);
Check(user.PasswordHash != request.Password && new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, request.Password) != PasswordVerificationResult.Failed, "user creation hashes password");
foreach (var role in new[] { "Admin", "Sales", "Service" })
{
    user.RoleName = role;
    var result = await auth.ValidateLoginAsync(request);
    Check(result.Success && result.UserId == 7 && result.RoleName == role, $"{role} login identity");
}
var wrong = await auth.ValidateLoginAsync(new LoginRequest { Username = "admin", Password = "wrong" });
Check(!wrong.Success && wrong.ErrorMessage == unknown.ErrorMessage, "wrong password rejected without revealing account existence");
user.IsActive = false;
var inactive = await auth.ValidateLoginAsync(request);
Check(!inactive.Success && inactive.ErrorCode == LoginResult.AccountInactive && inactive.ErrorMessage == LoginResult.InactiveMessage,
    "inactive user receives contact-administrator message after password verification");
var inactiveWrong = await auth.ValidateLoginAsync(new LoginRequest { Username = "admin", Password = "wrong" });
Check(!inactiveWrong.Success && inactiveWrong.ErrorCode is null && inactiveWrong.ErrorMessage == unknown.ErrorMessage,
    "wrong password does not reveal inactive account status");
user.IsActive = true;
user.RoleIsActive = false;
Check((await auth.ValidateLoginAsync(request)).ErrorCode == LoginResult.AccountInactive, "inactive role rejected with contact-administrator message");
user.RoleIsActive = true;
user.PasswordHash = "not a valid hash";
Check(!(await auth.ValidateLoginAsync(request)).Success, "malformed hash rejected");

static void Check(bool passed, string name)
{
    if (!passed) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
}

sealed class FakeUsers : IUserRepository
{
    private User? _user;
    public Task<User?> GetByUsernameAsync(string username) => Task.FromResult(_user?.Username == username ? _user : null);
    public Task<User?> GetByIdAsync(int userId) => Task.FromResult(_user?.UserId == userId ? _user : null);
    public Task<IEnumerable<User>> GetAllAsync() => Task.FromResult<IEnumerable<User>>(_user is null ? [] : [_user]);
    public Task<int> CreateAsync(User user) { _user = user; return Task.FromResult(user.UserId); }
    public Task<bool> UpdateAsync(User user) { _user = user; return Task.FromResult(true); }
}
