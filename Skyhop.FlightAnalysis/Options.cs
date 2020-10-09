﻿using NetTopologySuite.Geometries;
using Skyhop.FlightAnalysis.Models;
using System;
using System.Collections.Generic;

namespace Skyhop.FlightAnalysis
{
    public abstract class Options
    {
        public Func<Point, double, IEnumerable<Runway>> NearbyRunwayAccessor { get; set; }

        public bool MinifyMemoryPressure { get; set; }

        public int MinimumRequiredPositionUpdateCount { get; set; } = 5;
    }
}
