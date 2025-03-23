using System.Text.Json;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RedisCacheSample.Database;
using RedisCacheSample.Models;
using StackExchange.Redis;

namespace RedisCacheSample.Controllers;

[ApiController]
[Route("api/v1/")]
public class UserController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConnectionMultiplexer _redis;
    private const string RedisListKey = "users_list";

    public UserController(AppDbContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _redis = redis;
    }

    [HttpGet("users/generate")]
    public async Task<ActionResult<string>> GenerateUsers()
    {
        try
        {
            var user = new Faker<Users>()
                .RuleFor(u => u.FirstName, f => f.Name.FirstName())
                .RuleFor(u => u.LastName, f => f.Name.LastName())
                .RuleFor(u => u.Username, (f, u) =>
                    f.Internet.UserName(u.FirstName, u.LastName))
                .RuleFor(u => u.Email, (f, u) =>
                    f.Internet.Email(u.FirstName, u.LastName))
                .RuleFor(u => u.password, f => f.Internet.Password());
            List<Users> users = user.Generate(100_000);
            foreach (var u in users)
            {
                _context.Users.Add(u);
            }

            await _context.SaveChangesAsync();
            return "User generated";
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    [HttpGet("users/string")]
    public async Task<ActionResult<IEnumerable<Users>>> GetUsers()
    {
        const string cacheKey = "all_users";
        var db = _redis.GetDatabase();

        // Try to get cached data from Redis.
        string? cachedUsers = await db.StringGetAsync(cacheKey);

        if (!string.IsNullOrEmpty(cachedUsers))
        {
            // Deserialize cached JSON to list of Users.
            var users = JsonSerializer.Deserialize<IEnumerable<Users>>(cachedUsers);
            return Ok(users);
        }

        // Cache miss: Retrieve users from the database.
        var dbUsers = await _context.Users.ToListAsync();

        // Serialize to JSON and store it in Redis with an expiration time.
        var serializedUsers = JsonSerializer.Serialize(dbUsers);
        await db.StringSetAsync(cacheKey, serializedUsers, TimeSpan.FromSeconds(30));

        return Ok(dbUsers);
    }

    [HttpGet("users/list")]
    public async Task<ActionResult<IEnumerable<Users>>> GetUsersByList()
    {
        var db = _redis.GetDatabase();
        // Retrieve all entries in the list
        RedisValue[] cachedUsers = await db.ListRangeAsync(RedisListKey, 0, -1);

        // If we have cached users, deserialize them
        if (cachedUsers.Length > 0)
        {
            List<Users> users = new();
            foreach (var redisValue in cachedUsers)
            {
                // Deserialize each JSON string into a Users object.
                var user = JsonSerializer.Deserialize<Users>(redisValue!);
                if (user is not null)
                {
                    users.Add(user);
                }
            }

            return Ok(users);
        }
        // Fallback: If the list is empty, load from the database and fill the Redis list.
        var dbUsers = await _context.Users.ToListAsync();

        // For each user, add to the Redis List.
        foreach (var serializedUser in dbUsers.Select(user => JsonSerializer.Serialize(user)))
        {
            await db.ListRightPushAsync(RedisListKey, serializedUser);
        }

        return Ok(dbUsers);
    }

    [HttpPost("users")]
    public async Task<ActionResult<Users>> AddUser(Users newUser)
    {
        // Save the new user in the database.
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        // Append the new user to the Redis List after saving to the database.
        var db = _redis.GetDatabase();
        string serializedUser = JsonSerializer.Serialize(newUser);

        // Append to the end using ListRightPush.
        await db.ListRightPushAsync(RedisListKey, serializedUser);

        return Ok(newUser);
    }
}