using System;
using System.Collections.Generic;
using System.Linq;
using PrettyConvenientMenu.UI.Common;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Composite storage buffer representing an aggregation of multiple physical landings for the same cargo.
    /// Dynamically computes summary amounts, capacities, and rates from child landing buffers.
    /// </summary>
    public sealed class CompositeCommercialBuffer : CommercialBuffer, ICompositeBuffer<CommercialBuffer>
    {
        private readonly List<CommercialBuffer> _children = new List<CommercialBuffer>();

        public IReadOnlyList<CommercialBuffer> SubBuffers => _children;
        IReadOnlyList<BufferInfo> ICompositeBuffer.SubBuffers => _children;

        public CompositeCommercialBuffer(CommercialBuffer initialLanding)
        {
            CargoType = initialLanding.CargoType;
            CargoDisplayName = initialLanding.CargoDisplayName;
            Units = initialLanding.Units;
            Role = initialLanding.Role;

            Add(initialLanding);
        }

        public void Add(CommercialBuffer child)
        {
            (child.SwatchColor, child.SwatchHex) = LandingColors.GetSwatch(_children.Count);
            _children.Add(child);
        }

        private float _directActiveInbound;
        private float _directActiveOutbound;
        private float _directScheduledInbound;
        private float _directCommittedOutbound;

        public override float CurrentAmount => _children.Sum(c => c.CurrentAmount);
        public override float MaxCapacity => _children.Sum(c => c.MaxCapacity);
        public override float DailyRate => _children.Sum(c => c.DailyRate);

        public override float ActiveInbound
        {
            get => _children.Sum(c => c.ActiveInbound) + _directActiveInbound;
            set => _directActiveInbound = value;
        }

        public override float ActiveOutbound
        {
            get => _children.Sum(c => c.ActiveOutbound) + _directActiveOutbound;
            set => _directActiveOutbound = value;
        }

        public override float ScheduledInbound
        {
            get => _children.Sum(c => c.ScheduledInbound) + _directScheduledInbound;
            set => _directScheduledInbound = value;
        }

        public override float CommittedOutbound
        {
            get => _children.Sum(c => c.CommittedOutbound) + _directCommittedOutbound;
            set => _directCommittedOutbound = value;
        }

        public override TimeSpan? RunwayDuration
        {
            get
            {
                float rate = DailyRate;
                if (rate <= 0.001f) return null;
                float days = IsInput
                    ? CurrentAmount / rate
                    : Mathf.Max(0f, MaxCapacity - CurrentAmount) / rate;
                return TimeSpan.FromDays(days);
            }
        }
    }
}
