using System;
using System.Collections.Generic;
using Model.Definition.Data;
using Model.Ops;
using Model.Ops.Definition;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.RollingStock;
using PrettyConvenientMenu.Data.Spurs;
using UnityEngine;

namespace PrettyConvenientMenu.Data.Helpers
{
    public static class BufferHelper
    {
        public static CommercialBuffer GetOrAddCommercialBuffer(
            List<BufferInfo> buffers,
            string loadId,
            string displayName,
            LoadUnits units,
            bool isInput)
        {
            BufferRole role = isInput ? BufferRole.CommercialInput : BufferRole.CommercialOutput;
            foreach (BufferInfo buf in buffers)
            {
                if (buf is CommercialBuffer commercial && commercial.CargoType == loadId && commercial.Role == role)
                {
                    return commercial;
                }
            }

            CommercialBuffer newBuffer = new CommercialBuffer
            {
                CargoType = loadId,
                CargoDisplayName = displayName,
                Units = units,
                Role = role
            };
            buffers.Add(newBuffer);
            return newBuffer;
        }

        public static CommercialBuffer GetOrAddCommercialBuffer(List<BufferInfo> buffers, Load load, bool isInput)
        {
            return GetOrAddCommercialBuffer(buffers, load.id, load.description ?? load.id, load.units, isInput);
        }

        public static ReserveBuffer GetOrAddReserveBuffer(
            List<BufferInfo> buffers,
            string loadId,
            string displayName,
            LoadUnits units,
            BufferRole role)
        {
            foreach (BufferInfo buf in buffers)
            {
                if (buf is ReserveBuffer reserve && reserve.CargoType == loadId && reserve.Role == role)
                {
                    return reserve;
                }
            }

            ReserveBuffer newBuffer = new ReserveBuffer
            {
                CargoType = loadId,
                CargoDisplayName = displayName,
                Units = units,
                Role = role
            };
            buffers.Add(newBuffer);
            return newBuffer;
        }

        public static ReserveBuffer GetOrAddReserveBuffer(List<BufferInfo> buffers, Load load, BufferRole role)
        {
            return GetOrAddReserveBuffer(buffers, load.id, load.description ?? load.id, load.units, role);
        }

        public static SpurCarGroup GetOrAddSpurGroup(List<SpurCarGroup> groups, string trackName, SpurRole role)
        {
            foreach (SpurCarGroup group in groups)
            {
                if (group.TrackName == trackName && group.Role == role)
                {
                    return group;
                }
            }

            SpurCarGroup newGroup = new SpurCarGroup
            {
                TrackName = trackName,
                Role = role
            };
            groups.Add(newGroup);
            return newGroup;
        }

        public static float FindMaxStorageForLoad(List<IndustryComponent>? components, Load? targetLoad)
        {
            if (components == null || targetLoad == null)
            {
                return 100f;
            }

            foreach (IndustryComponent comp in components)
            {
                if (comp is IndustryUnloader unloader && unloader.load == targetLoad)
                {
                    return Mathf.Max(0.001f, unloader.maxStorage);
                }
                if (comp is IndustryLoader loader && loader.load == targetLoad)
                {
                    return Mathf.Max(0.001f, loader.maxStorage);
                }
            }
            return 100f;
        }

        public static BufferInfo? FindMatchingBuffer(List<BufferInfo> buffers, Load load, bool isUnload)
        {
            foreach (BufferInfo buf in buffers)
            {
                if (string.Equals(buf.CargoType, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    if (buf is CommercialBuffer comm)
                    {
                        if (comm.IsInput == isUnload) return comm;
                    }
                    else
                    {
                        return buf;
                    }
                }
            }
            return null;
        }

        public static ReserveBuffer? FindRepairBuffer(
            IReadOnlyList<IndustryComponent> componentsOnSpur,
            List<BufferInfo> buffers,
            SpurCapabilities caps)
        {
            if (!caps.CanRepair) return null;

            foreach (IndustryComponent comp in componentsOnSpur)
            {
                if (comp is RepairTrack rt)
                {
                    ReserveBuffer buffer = GetOrAddReserveBuffer(buffers, rt.repairPartsLoadId, "Parts", LoadUnits.Pounds, BufferRole.MaintenanceReserve);
                    buffer.IsRepairParts = true;
                    return buffer;
                }
            }
            return null;
        }
    }
}
