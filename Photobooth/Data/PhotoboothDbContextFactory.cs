using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Photobooth.Data
{
    /// <summary>
    /// Allows dotnet-ef CLI tools to create a PhotoboothDbContext at design time
    /// without launching the WPF app (which requires STA and full Windows startup).
    /// </summary>
    public class PhotoboothDbContextFactory : IDesignTimeDbContextFactory<PhotoboothDbContext>
    {
        public PhotoboothDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PhotoboothDbContext>()
                .UseSqlite("Data Source=photobooth.db")
                .Options;
            return new PhotoboothDbContext(options);
        }
    }
}
