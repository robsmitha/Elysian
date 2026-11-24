using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Models;

namespace Elysian.Application.Interfaces
{
    public interface IPlacementRenderer
    {
        /// <summary>
        /// Decodes an original once for every slot it fills: upright, private metadata stripped
        /// </summary>
        Task<IPlacementSource> LoadAsync(Stream original, CancellationToken cancellationToken = default);

        /// <summary>
        /// A plain frame at the slot's ratio, for a slot with no photo at all
        /// </summary>
        /// <param name="transparent">See-through, so the page's own frame shows; otherwise a soft ivory</param>
        Task<IReadOnlyList<PlacementVariant>> RenderNeutralAsync(PlacementSlot slot, bool transparent = false, CancellationToken cancellationToken = default);
    }

    public interface IPlacementSource : IDisposable
    {
        /// <summary>
        /// Crops to the slot's ratio around the focal point (0..1 from the top-left) and encodes each width,
        /// smallest first
        /// </summary>
        Task<IReadOnlyList<PlacementVariant>> RenderAsync(PlacementSlot slot, double focusX, double focusY,
            CancellationToken cancellationToken = default);
    }
}
