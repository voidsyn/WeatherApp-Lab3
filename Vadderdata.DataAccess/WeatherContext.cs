using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Core;

namespace DataAccess
{
    public class WeatherContext : DbContext
    {
        public DbSet<WeatherReading> Readings { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=Fuad_WeatherApp.db");
        }
    }
}
