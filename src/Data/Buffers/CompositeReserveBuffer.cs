using System.Collections.Generic;
using System.Linq;
using PrettyConvenientMenu.UI.Common;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Composite storage buffer representing an aggregation of multiple physical stands/chutes for terminal reserves.
    /// </summary>
    public sealed class CompositeReserveBuffer : ReserveBuffer, ICompositeBuffer<ReserveBuffer>
    {
        private readonly List<ReserveBuffer> _children = new List<ReserveBuffer>();

        public IReadOnlyList<ReserveBuffer> SubBuffers => _children;
        IReadOnlyList<BufferInfo> ICompositeBuffer.SubBuffers => _children;

        public CompositeReserveBuffer(ReserveBuffer initialStand)
        {
            CargoType = initialStand.CargoType;
            CargoDisplayName = initialStand.CargoDisplayName;
            Units = initialStand.Units;
            Role = initialStand.Role;
            IsRepairParts = initialStand.IsRepairParts;

            Add(initialStand);
        }

        public void Add(ReserveBuffer child)
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
        public override float ProgramDemand => _children.Sum(c => c.ProgramDemand);
        public override float SpotDemand => _children.Sum(c => c.SpotDemand);

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
    }
}
