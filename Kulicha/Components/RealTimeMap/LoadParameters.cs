csharp
using System.Collections.Generic;

namespace Kulicha.Components.RealTimeMap
{
    public class LoadParameters
    {
        public Location location { get; set; }
        public int zoomLevel { get; set; }
        public Basemap basemap { get; set; }

        public LoadParameters()
        {
            location = new Location { latitude = 0, longitude = 0 };
            zoomLevel = 1;
        }
    }
}