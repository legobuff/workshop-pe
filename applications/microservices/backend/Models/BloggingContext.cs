using System.Collections.Generic;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace microservices.backend
{
    public class BloggingContext : DbContext
    {
        public DbSet<Post> Posts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Filename=./blog.db");
        }
    }
}