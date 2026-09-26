using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
