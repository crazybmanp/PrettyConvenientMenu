using System.Collections.Generic;
using Model.Ops;

namespace PrettyConvenientMenu.Data
{
    internal readonly struct SpurTrackCar
    {
        public readonly IOpsCar OpsCar;
        public readonly float Quantity;
        public readonly float Capacity;
        public readonly int TrackOrder;

        public SpurTrackCar(IOpsCar opsCar, float quantity, float capacity, int trackOrder)
        {
            OpsCar = opsCar;
            Quantity = quantity;
            Capacity = capacity;
            TrackOrder = trackOrder;
        }
    }

    internal sealed class UnloadOrderAscending : IComparer<SpurTrackCar>
    {
        public static readonly UnloadOrderAscending Instance = new UnloadOrderAscending();

        public int Compare(SpurTrackCar x, SpurTrackCar y)
        {
            int cmp = x.Quantity.CompareTo(y.Quantity);
            if (cmp != 0)
            {
                return cmp;
            }

            return x.TrackOrder.CompareTo(y.TrackOrder);
        }
    }

    internal sealed class LoadOrderDescending : IComparer<SpurTrackCar>
    {
        public static readonly LoadOrderDescending Instance = new LoadOrderDescending();

        public int Compare(SpurTrackCar x, SpurTrackCar y)
        {
            int cmp = y.Quantity.CompareTo(x.Quantity);
            if (cmp != 0)
            {
                return cmp;
            }

            return x.TrackOrder.CompareTo(y.TrackOrder);
        }
    }
}
