using System.Collections.Generic;

namespace PrettyConvenientMenu.Data.Buffers
{
    /// <summary>
    /// Universal interface for any buffer that contains multi-spot physical sub-buffers (landings, stands, chutes).
    /// </summary>
    public interface ICompositeBuffer
    {
        /// <summary>
        /// Read-only collection of child physical sub-buffers that compose this aggregate buffer.
        /// </summary>
        IReadOnlyList<BufferInfo> SubBuffers { get; }
    }

    /// <summary>
    /// Strongly-typed covariant composite buffer interface.
    /// </summary>
    /// <typeparam name="T">The concrete child buffer type.</typeparam>
    public interface ICompositeBuffer<out T> : ICompositeBuffer where T : BufferInfo
    {
        new IReadOnlyList<T> SubBuffers { get; }
    }
}
