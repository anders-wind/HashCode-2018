﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windemann.HashCode.Qualification.Heuristics;
using Windemann.HashCode.Qualification.Model;

namespace Windemann.HashCode.Qualification
{
    public class QualificationSolverBandB : IQualificationSolver
    {
        private readonly QualificationInstance _instance;
        private readonly QualificationSolverSingleVehicle _upperHeuristic;
        private readonly CancellationToken _cancellationToken;

        public QualificationSolverBandB(QualificationInstance instance, CancellationToken cancellationToken)
        {
            _instance = instance;
            _upperHeuristic = new QualificationSolverSingleVehicle(_instance);
            _cancellationToken = cancellationToken;
        }
        
        public QualificationResult Solve()
        {
            var vehicles = new List<Vehicle>();
            for (int i = 0; i < _instance.NumberOfVehicles; i++)
            {
                vehicles.Add(new Vehicle());
            }
            var ridesLeft = new SortedSet<Ride>(new RideComparer());
            foreach (var instanceRide in _instance.Rides)
            {
                ridesLeft.Add(instanceRide);
            }

            var root = new BbNode(_instance.NumberOfVehicles);
            root.LowerBound = LowerBound(root);
            root.UpperBound = UpperBound(root);
            root.Vehicles = vehicles;
            root.Rides = ridesLeft;

            var priorityQueue = new SortedSet<BbNode>();
            var bestNode = root;
            var incumbent = 0;

            priorityQueue.Add(root);
            while (priorityQueue.Any())
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    // TODO: Make the solution from this.
                    throw new NotImplementedException();
                }
                
                var node = priorityQueue.Min;
                priorityQueue.Remove(node);
                
                if(node.UpperBound <= incumbent)
                    continue;

                var (take, stay) = GetChildren(node);
                take.UpperBound = UpperBound(take);
                take.LowerBound = LowerBound(take);
                stay.UpperBound = UpperBound(stay);
                stay.LowerBound = LowerBound(stay);

                if (node.LowerBound > incumbent)
                {
                    incumbent = node.LowerBound;
                    bestNode = node;
                }

                if (!MustBePruned(take, incumbent))
                    priorityQueue.Add(take);

                if (!MustBePruned(stay, incumbent))
                    priorityQueue.Add(stay);
            }

            var result = new QualificationResult(_instance);
            foreach (var bestNodeAssignment in bestNode.Assignments)
            {
                result.AddAssignment(bestNodeAssignment.VehicleId, bestNodeAssignment.RideId);
            }
            return result;
        }

        private bool MustBePruned(BbNode node, int incumbent)
        {
            if (node.UpperBound <= incumbent)
                return true;
            if (node.UpperBound == node.LowerBound)
                return true;
            // maybe more
            return false;
        }


        private (BbNode take, BbNode stay) GetChildren(BbNode node)
        {
            Ride picked = null;
            Vehicle v = null;
            foreach (var nodeVehicle in node.Vehicles)
            {
                var ride = node.Rides.FirstOrDefault(x =>
                    nodeVehicle.PossiblePickupTime(x) < Math.Min(x.LatestFinish, _instance.NumberOfSteps)
                    && !node.Conflicts[nodeVehicle.Id].Contains(x.Id));
                if (ride != null)
                {
                    v = nodeVehicle;
                    picked = ride;
                    break;
                }
            }

            if (picked == null)
            {
                return (new BbNode(_instance.NumberOfVehicles), new BbNode(_instance.NumberOfVehicles)); // they will have bad bounds
            }

            var stay = new BbNode(node);
            var take = new BbNode(node);

            stay.Conflicts[v.Id].Add(picked.Id);

            take.Rides.Remove(picked);
            var takeVehicle = take.Vehicles.Find(x => x.Id == v.Id);
            takeVehicle.TimeAvailable += takeVehicle.PossiblePickupTime(picked) + picked.Distance;

            take.Assignments.Add(new Assignment()
            {
                RideId = picked.Id,
                VehicleId = takeVehicle.Id,
                Value = picked.Distance
            });

            return (take, stay);
        }

        private int UpperBound(BbNode node)
        {
            return node.Vehicles.Sum(vehicle => _upperHeuristic.ChooseRidesForVehicle(vehicle, node.Rides).Sum(r => r.Score));
        }

        private int LowerBound(BbNode node)
        {
            var solver = new QualificationSolverGreedy(_instance);

            return solver.Solve(node.Vehicles, node.Rides).Sum(r => r.Score);
        }
    }
}
