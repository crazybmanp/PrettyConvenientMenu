using System;
using System.Collections;
using System.Collections.Generic;
using Model.Ops;
using Model.Ops.Definition;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// First-class domain collection managing facility buffers.
    /// Encapsulates lookup, shared-storage deduplication, spur swatch indexing, and leaf-to-composite aggregation.
    /// </summary>
    public sealed class BufferCollection : IReadOnlyList<BufferInfo>
    {
        private readonly List<BufferInfo> _buffers = new List<BufferInfo>(8);
        private readonly Dictionary<string, string> _swatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _buffers.Count;
        public BufferInfo this[int index] => _buffers[index];

        /// <summary>
        /// Adds a commercial freight buffer to the collection.
        /// </summary>
        public void AddCommercial(
            Load load,
            bool isInput,
            float currentStock,
            float maxCapacity,
            float dailyRate,
            string? prefix = null,
            string? qualifier = null)
        {
            Add(new CommercialBuffer(load, isInput, currentStock, maxCapacity, dailyRate, prefix, qualifier));
        }

        /// <summary>
        /// Adds an engine supply or maintenance reserve buffer to the collection.
        /// </summary>
        public void AddReserve(
            Load load,
            BufferRole role,
            float currentStock,
            float maxCapacity,
            string? prefix = null,
            string? qualifier = null,
            bool isRepairParts = false)
        {
            ReserveBuffer buf = new ReserveBuffer(load, role, currentStock, maxCapacity, prefix, qualifier)
            {
                IsRepairParts = isRepairParts
            };
            Add(buf);
        }

        /// <summary>
        /// Adds a buffer to the facility collection.
        /// If a buffer for the same cargo/role already exists:
        /// - If it's a distinct physical storage pool (subIdentifier/prefix), promotes to or appends to a composite buffer.
        /// - If it's shared storage, deduplicates without double-counting.
        /// </summary>
        public void Add(BufferInfo newBuffer)
        {
            int index = _buffers.FindIndex(b => b.CargoType == newBuffer.CargoType && b.Role == newBuffer.Role);

            if (index < 0)
            {
                _buffers.Add(newBuffer);
                return;
            }

            BufferInfo existing = _buffers[index];

            // Deduplication rule: If the storage pool prefix matches or is shared (null), do not accumulate.
            // If the existing buffer was an empty placeholder, update it.
            if (string.IsNullOrEmpty(newBuffer.StoragePrefix) ||
                string.Equals(existing.StoragePrefix, newBuffer.StoragePrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (existing.MaxCapacity <= 0.001f && newBuffer.MaxCapacity > 0.001f)
                {
                    _buffers[index] = newBuffer;
                }
                return;
            }

            // Multi-spot aggregation: promote leaf to composite or append to existing composite
            BufferInfo updated = PromoteOrAppend(existing, newBuffer);
            _buffers[index] = updated;

            if (updated is ICompositeBuffer composite)
            {
                foreach (BufferInfo sub in composite.SubBuffers)
                {
                    RegisterSwatch(sub);
                }
            }
        }

        private static BufferInfo PromoteOrAppend(BufferInfo existing, BufferInfo incoming)
        {
            switch (existing)
            {
                case CompositeCommercialBuffer compComm when incoming is CommercialBuffer comm:
                    compComm.Add(comm);
                    return compComm;

                case CommercialBuffer singleComm when incoming is CommercialBuffer comm:
                    CompositeCommercialBuffer newCompComm = new CompositeCommercialBuffer(singleComm);
                    newCompComm.Add(comm);
                    return newCompComm;

                case CompositeReserveBuffer compRes when incoming is ReserveBuffer res:
                    compRes.Add(res);
                    return compRes;

                case ReserveBuffer singleRes when incoming is ReserveBuffer res:
                    CompositeReserveBuffer newCompRes = new CompositeReserveBuffer(singleRes);
                    newCompRes.Add(res);
                    return newCompRes;

                default:
                    return existing;
            }
        }

        private void RegisterSwatch(BufferInfo buffer)
        {
            if (string.IsNullOrEmpty(buffer.SwatchHex)) return;

            string hex = buffer.SwatchHex!;
            if (!string.IsNullOrEmpty(buffer.StoragePrefix))
            {
                _swatches[buffer.StoragePrefix!] = hex;
            }
            if (!string.IsNullOrEmpty(buffer.Qualifier))
            {
                _swatches[buffer.Qualifier!] = hex;
            }
        }

        /// <summary>
        /// Finds the matching swatch hex code for any component on a spur track.
        /// </summary>
        public string? FindSwatch(IEnumerable<IndustryComponent> components)
        {
            foreach (IndustryComponent comp in components)
            {
                if (!string.IsNullOrEmpty(comp.subIdentifier) && _swatches.TryGetValue(comp.subIdentifier, out string hex))
                {
                    return hex;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the matching buffer for an industry component, automatically routing to the
        /// individual landing buffer or aggregate facility buffer based on component storage configuration.
        /// </summary>
        public BufferInfo? FindMatching(IndustryComponent component, Load? overrideLoad = null)
        {
            if (component == null) return null;

            Load? load = overrideLoad;
            bool isUnload = false;
            BufferRole? targetRole = null;

            switch (component)
            {
                case IndustryUnloader unloader:
                    load ??= unloader.load;
                    isUnload = true;
                    targetRole = BufferRole.CommercialInput;
                    break;
                case StorageUnloader:
                    isUnload = true;
                    targetRole = null;
                    break;
                case IndustryLoaderBase loader:
                    load ??= loader.load;
                    isUnload = false;
                    targetRole = BufferRole.CommercialOutput;
                    break;
                case IndustrySeller seller:
                    load ??= !string.IsNullOrEmpty(seller.loadId) ? CarLoadLibrary.GetLoad(seller.loadId) : null;
                    isUnload = false;
                    targetRole = BufferRole.MaintenanceReserve;
                    break;
                case StorageLoader:
                    isUnload = false;
                    targetRole = null;
                    break;
                default:
                    if (component.GetType().Name == "GeneralIndustryLoader")
                    {
                        load ??= component.GetType().GetProperty("TargetLoad")?.GetValue(component) as Load;
                        isUnload = false;
                        targetRole = BufferRole.CommercialOutput;
                    }
                    break;
            }

            if (load == null) return null;

            string? prefix = !component.sharedStorage ? component.subIdentifier : null;
            return FindMatching(load, isUnload, prefix, targetRole);
        }

        private BufferInfo? FindMatching(Load load, bool isUnload, string? storagePrefix = null, BufferRole? targetRole = null)
        {
            foreach (BufferInfo buf in _buffers)
            {
                if (string.Equals(buf.CargoType, load.id, StringComparison.OrdinalIgnoreCase))
                {
                    if (targetRole.HasValue && buf.Role != targetRole.Value)
                    {
                        continue;
                    }

                    bool match = buf is not CommercialBuffer comm || (comm.IsInput == isUnload);
                    if (!match) continue;

                    if (!string.IsNullOrEmpty(storagePrefix))
                    {
                        if (string.Equals(buf.StoragePrefix, storagePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            return buf;
                        }

                        if (buf is ICompositeBuffer composite)
                        {
                            foreach (BufferInfo sub in composite.SubBuffers)
                            {
                                if (string.Equals(sub.StoragePrefix, storagePrefix, StringComparison.OrdinalIgnoreCase))
                                {
                                    return sub;
                                }
                            }
                        }
                        continue;
                    }

                    return buf;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the active repair parts maintenance buffer, if present.
        /// </summary>
        public ReserveBuffer? FindRepairBuffer()
        {
            foreach (BufferInfo buf in _buffers)
            {
                if (buf is ReserveBuffer { IsRepairParts: true } reserve)
                {
                    return reserve;
                }
            }
            return null;
        }

        public BufferInfo? Find(Predicate<BufferInfo> match) => _buffers.Find(match);

        public void Clear()
        {
            _buffers.Clear();
            _swatches.Clear();
        }

        public IEnumerator<BufferInfo> GetEnumerator() => _buffers.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
