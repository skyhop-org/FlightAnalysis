﻿using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;

namespace Skyhop.FlightAnalysis
{
    public class FlightContextOptions : Options
    {
        public Func<Point, double, IEnumerable<FlightContext>> NearbyAircraftAccessor { get; set; }
        public string AircraftId { get; set; }
        
    }
}
