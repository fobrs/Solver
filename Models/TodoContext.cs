using Microsoft.EntityFrameworkCore;

namespace Solver.Models;

public class TodoDb : DbContext
{
    public TodoDb(DbContextOptions<TodoDb> options)
        : base(options)
    {
    }

    public DbSet<SolverConfig> Todos => Set<SolverConfig>();
}
