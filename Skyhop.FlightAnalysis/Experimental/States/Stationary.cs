﻿using System.Linq;

namespace Skyhop.FlightAnalysis.Experimental
{
    internal static partial class MachineStates
    {
        internal static void Stationary(this FlightContext context)
        {
            if (context.CurrentPosition == null) return;

            if (context.CurrentPosition.Speed > 30)
            {
                // Walk back to when the speed was 0
                var start = context.Flight.PositionUpdates.Where(q => q.TimeStamp < context.CurrentPosition.TimeStamp && (q.Speed == 0 || double.IsNaN(q.Speed)))
                    .OrderByDescending(q => q.TimeStamp)
                    .FirstOrDefault();

                if (start == null)
                {
                    // The flight was already in progress, or we could not find the starting point (trees in line of sight?)

                    // Create an estimation about the departure time. Unless contact happens high in the sky
                }
                if (start != null)
                {
                    context.Flight.StartTime = start.TimeStamp;

                    // Remove points not related to this flight
                    context.Flight.PositionUpdates
                        .Where(q => q.TimeStamp < context.Flight.StartTime.Value)
                        .ToList()
                        .ForEach(q => context.Flight.PositionUpdates.Remove(q));
                }

                context.InvokeOnTakeoffEvent();

                context.StateMachine.Fire(FlightContext.Trigger.Depart);
            }
        }
    }
}
