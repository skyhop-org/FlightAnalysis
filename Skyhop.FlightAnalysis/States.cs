﻿using Skyhop.FlightAnalysis.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skyhop.FlightAnalysis
{
    internal static partial class MachineStates
    {
        internal static void Initialize(this FlightContext context)
        {
                context.LatestTimeStamp = DateTime.MinValue;

                context.Flight = new Flight
                {
                    Aircraft = context.AircraftId
                };

            context.StateMachine.Fire(FlightContext.Trigger.Next);
        }

        internal static void ProcessNextPoint(this FlightContext context)
        {
            if (context.Flight == null || context.Flight.EndTime != null)
            {
                // Reset the context
                context.StateMachine.Fire(FlightContext.Trigger.Initialize);
                    
                return;
            }

            if (!context.PriorityQueue.Any())
            {
                context.StateMachine.Fire(FlightContext.Trigger.Standby);
                return;
            }

            var position = context.PriorityQueue.Dequeue();

            if (context.Flight.PositionUpdates
                .Skip(context.Flight.PositionUpdates.Count - 10)
                .Take(10)
                .Any(q => q.Latitude == position.Latitude
                    && q.Longitude == position.Longitude))
            {
                context.StateMachine.Fire(FlightContext.Trigger.Next);
                return;
            }

            // ToDo: Put this part in a state step
            position = States.States.NormalizeData(context, position);

            if (position == null
                || double.IsNaN(position.Heading)
                || double.IsNaN(position.Speed))
            {
                context.StateMachine.Fire(FlightContext.Trigger.Next);

                return;
            }
            else
            {
                context.Flight.PositionUpdates.Add(position);
                context.CleanupDataPoints();
            }

            if (context.LatestTimeStamp == DateTime.MinValue) context.LatestTimeStamp = position.TimeStamp;

            if (context.Flight.StartTime == null)
            {
                // Just keep the buffer small by removing points older then 2 minutes. The flight hasn't started anyway
                context.Flight.PositionUpdates
                        .Where(q => q.TimeStamp < position.TimeStamp.AddMinutes(-2))
                        .ToList()
                        .ForEach(q => context.Flight.PositionUpdates.Remove(q));
            }
            else if (context.LatestTimeStamp < position.TimeStamp.AddHours(-8))
            {
                context.InvokeOnCompletedWithErrorsEvent();
                    
                context.StateMachine.Fire(FlightContext.Trigger.Initialize);

                context.LatestTimeStamp = position.TimeStamp;
                return;
            }

            context.LatestTimeStamp = position.TimeStamp;

            context.StateMachine.Fire(FlightContext.Trigger.ResolveState);
        }

        internal static void DetermineFlightState(this FlightContext context)
        {
            var positionUpdate = context.Flight.PositionUpdates.LastOrDefault();

            // Flight has ended when the speed = 0 and the heading = 0
            if (positionUpdate.Speed == 0)
            {
                /*
                    * If a flight has been in progress, end the flight.
                    * 
                    * When the aircraft has been registered mid flight the departure
                    * location is unknown, and so is the time. Therefore look at the
                    * flag which is set to indicate whether the departure location has
                    * been found.
                    * 
                    * ToDo: Also check the vertical speed as it might be an indication
                    * that the flight is still in progress! (Aerobatic stuff and so)
                    */

                // We might as well check for Context.Flight.DepartureInfoFound != null, I think
                if (context.Flight.StartTime != null ||
                    context.Flight.DepartureInfoFound == false)
                {
                    context.Flight.EndTime = positionUpdate.TimeStamp;
                    context.StateMachine.Fire(FlightContext.Trigger.ResolveArrival);
                }

                context.StateMachine.Fire(FlightContext.Trigger.Next);
                
                return;
            }
            
            // This part is about the departure
            if (context.Flight.StartTime == null
                && positionUpdate.Speed > 30
                && context.Flight.DepartureInfoFound != false)
            {
                // We have to start the flight

                // Walk back to when the speed was 0
                var start = context.Flight.PositionUpdates.Where(q => q.TimeStamp < positionUpdate.TimeStamp && (q.Speed == 0 || double.IsNaN(q.Speed)))
                    .OrderByDescending(q => q.TimeStamp)
                    .FirstOrDefault();

                if (start == null)
                {
                    // This means that an aircraft is flying but the takeoff itself hasn't been recorded due to insufficient flarm coverage
                    context.Flight.DepartureInfoFound = false;
                    context.InvokeOnRadarContactEvent();
                }
                else
                {
                    context.Flight.DepartureInfoFound = true;
                    context.Flight.StartTime = start.TimeStamp;

                    // Remove the points we do not need. (From before the flight, for example during taxi)
                    context.Flight.PositionUpdates
                        .Where(q => q.TimeStamp < context.Flight.StartTime.Value)
                        .ToList()
                        .ForEach(q => context.Flight.PositionUpdates.Remove(q));
                }
            }

            if (context.Flight.StartTime != null
                && context.Flight.DepartureHeading == 0)
            {
                context.StateMachine.Fire(FlightContext.Trigger.ResolveDeparture);
            } 
            else if (context.Flight.DepartureInfoFound == true
                && context.Flight.LaunchMethod == LaunchMethod.Unknown)
            {
                context.StateMachine.Fire(FlightContext.Trigger.ResolveLaunchMethod);
            }

            context.StateMachine.Fire(FlightContext.Trigger.Next);
        }

        internal static void FindDepartureHeading(this FlightContext context)
        {
            var departure = context.Flight.PositionUpdates
                .Where(q => q.Heading != 0 && !double.IsNaN(q.Heading))
                .OrderBy(q => q.TimeStamp)
                .Take(5)
                .ToList();

            if (departure.Count < 5) return;

            context.Flight.DepartureHeading = Convert.ToInt16(departure.Average(q => q.Heading));
            context.Flight.DepartureLocation = departure.First().Location;

            if (context.Flight.DepartureHeading == 0) context.Flight.DepartureHeading = 360;

            context.InvokeOnTakeoffEvent();
        }

        internal static void FindArrivalHeading(this FlightContext context)
        {
            var arrival = context.Flight.PositionUpdates
                .Where(q => q.Heading != 0 && !double.IsNaN(q.Heading))
                .OrderByDescending(q => q.TimeStamp)
                .Take(5)
                .ToList();

            if (!arrival.Any()) return;

            context.Flight.ArrivalInfoFound = true;
            context.Flight.ArrivalHeading = Convert.ToInt16(arrival.Average(q => q.Heading));
            context.Flight.ArrivalLocation = arrival.First().Location;

            if (context.Flight.ArrivalHeading == 0) context.Flight.ArrivalHeading = 360;

            context.InvokeOnLandingEvent();
        }

        internal static void DetermineLaunchMethod(this FlightContext context)
        {
            /*
             * The launch method will be determined after the departure direction has been confirmed.
             * 
             * After this we'll do an elimination to find the launch method. Preffered order;
             * 
             * 1. Winch launch
             * 2. Aerotow
             * 3. Self launch
             * 
             * First we'll check whether we're dealing with a winch launch. In order to qualify for a winch launch;
             * - Average heading deviation no more than 15°
             * - Climb distance less than 2 kilometers
             * - Winch distance per meter altitude between 0.2 and 0.8?
             */

            var climbrate = new List<double>(context.Flight.PositionUpdates.Count);

            for (var i = 1; i < context.Flight.PositionUpdates.Count; i++)
            {
                var deltaTime = context.Flight.PositionUpdates[i].TimeStamp - context.Flight.PositionUpdates[i - 1].TimeStamp;

                if (deltaTime.TotalSeconds < 0.1) continue;

                var deltaAltitude = context.Flight.PositionUpdates[i].Altitude - context.Flight.PositionUpdates[i - 1].Altitude;

                climbrate.Add(deltaAltitude / deltaTime.TotalMinutes);
            }

            if (climbrate.Count < 21) return;

            var result = ZScore.StartAlgo(climbrate, 20, 2, 1);

            if (result.Signals.Any(q => q == -1))
            {
                context.Flight.LaunchMethod = LaunchMethod.Winch;
                context.InvokeOnLaunchCompletedEvent();
            }
        }
    }
}
