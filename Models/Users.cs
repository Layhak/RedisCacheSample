using System.ComponentModel.DataAnnotations;

namespace RedisCacheSample.Models;

public class Users
{
    [Key] public Guid Id { get; init; } = Guid.NewGuid();
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public string Username { get; init; }
    [EmailAddress] public string Email { get; init; }
    public string password { get; init; }
}