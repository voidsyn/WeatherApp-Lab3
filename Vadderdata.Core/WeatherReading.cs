using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core
{
    public class WeatherReading
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Location { get; set; } //"Inne" eller "Ute"
        public double Temperature { get; set; } //Celsius
        public int Humidity { get; set; } //Procent
    }
}
