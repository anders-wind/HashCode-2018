﻿using System;
using System.Collections.Generic;
using System.Linq;
using Windemann.HashCode.Qualification.Heuristics;
using Windemann.HashCode.Qualification.Model;

namespace Windemann.HashCode.Qualification
{
    public class QualificationSolverBandB : IQualificationSolver
    {
        private readonly QualificationInstance _instance;
        private readonly QualificationSolverSingleVehicle _upperHeuristic;

        public QualificationSolverBandB(QualificationInstance instance)
        {
            _instance = instance;
            _upperHeuristic = new QualificationSolverSingleVehicle(_instance);
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

            var root = new BbNode();
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


        private (BbNode node1, BbNode node2) GetChildren(BbNode node)
        {
            foreach (var nodeVehicle in node.Vehicles)
            {
                node.Rides.FirstOrDefault(x =>
                    nodeVehicle.PossiblePickupTime(x) < Math.Min(x.LatestFinish, _instance.NumberOfSteps));
            }

            return (node, node);
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
