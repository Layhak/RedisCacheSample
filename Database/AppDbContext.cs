using Microsoft.EntityFrameworkCore;
using RedisCacheSample.Models;

namespace RedisCacheSample.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
    public DbSet<Users> Users { get; set; }
}