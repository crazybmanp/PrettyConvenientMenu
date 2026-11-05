using System;
using Model.Ops.Definition;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Storage buffer for commercial factories. Tracks recipe roles, daily rate, and runway.
    /// </summary>
    public class CommercialBuffer : BufferInfo
    {
        public bool IsInput => Role == BufferRole.CommercialInput;
        public virtual float DailyRate { get; set; }
        public virtual TimeSpan? RunwayDuration { get; set; }

        public CommercialBuffer() { }

        public CommercialBuffer(
            Load load,
            bool isInput,
            float currentAmount,
            float maxCapacity,
            float dailyRate,
            string? storagePrefix = null,
            string? qualifier = null)
        {
            CargoType = load.id;
            CargoDisplayName = load.description ?? load.id;
            Units = load.units;
            Role = isInput ? BufferRole.CommercialInput : BufferRole.CommercialOutput;
            CurrentAmount = currentAmount;
            MaxCapacity = maxCapacity;
            DailyRate = dailyRate;
            StoragePrefix = storagePrefix;
            Qualifier = qualifier;
            UpdateRunway();
        }

        public virtual void UpdateRunway()
        {
            float rate = DailyRate;
            if (rate > 0.001f)
            {
                float days = IsInput
                    ? CurrentAmount / rate
                    : Mathf.Max(0f, MaxCapacity - CurrentAmount) / rate;
                RunwayDuration = TimeSpan.FromDays(days);
            }
            else
            {
                RunwayDuration = null;
            }
        }
    }
}
