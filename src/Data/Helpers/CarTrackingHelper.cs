using System;
using Model;
using Model.Ops;
using Model.Ops.Definition;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class CarTrackingHelper
    {
        public static (Load? load, float quantity, float capacity) GetCarCargo(Car? car, IOpsCar? opsCar)
        {
            if (ReferenceEquals(car, null) || car == null || car.Definition == null || car.Definition.LoadSlots == null)
            {
                return (null, 0f, 0f);
            }

            for (int i = 0; i < car.Definition.LoadSlots.Count; i++)
            {
                CarLoadInfo? info = car.GetLoadInfo(i);
                if (info.HasValue && !string.IsNullOrEmpty(info.Value.LoadId) && info.Value.Quantity > 0.001f)
                {
                    Load load = CarLoadLibrary.GetLoad(info.Value.LoadId);
                    float cap = car.Definition.LoadSlots[i].CalculateMaximumCapacity(load, car);
                    return (load, info.Value.Quantity, cap);
                }
            }

            return (null, 0f, 0f);
        }

        public static bool IsWaybilledHere(Waybill? waybill, Industry? industry, IndustryComponent? specificComponent = null)
        {
            if (!waybill.HasValue || industry == null)
            {
                return false;
            }

            if (specificComponent != null)
            {
                return string.Equals(waybill.Value.Destination.Identifier, specificComponent.Identifier, StringComparison.OrdinalIgnoreCase)
                    || waybill.Value.Destination.Equals((OpsCarPosition)specificComponent);
            }

            if (industry.Components != null)
            {
                foreach (IndustryComponent c in industry.Components)
                {
                    if (string.Equals(waybill.Value.Destination.Identifier, c.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (waybill.Value.Destination.Equals((OpsCarPosition)c))
                    {
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(industry.identifier) && !string.IsNullOrEmpty(waybill.Value.Destination.Identifier))
            {
                if (waybill.Value.Destination.Identifier.StartsWith(industry.identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsWaybilledFromHere(Waybill? waybill, Industry? industry)
        {
            if (!waybill.HasValue || industry == null || string.IsNullOrEmpty(industry.identifier))
            {
                return false;
            }

            return waybill.Value.Origin.HasValue
                && !string.IsNullOrEmpty(waybill.Value.Origin.Value.Identifier)
                && waybill.Value.Origin.Value.Identifier.StartsWith(industry.identifier, StringComparison.OrdinalIgnoreCase);
        }
    }
}
