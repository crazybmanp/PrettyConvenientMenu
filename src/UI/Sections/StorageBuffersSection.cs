using System;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.UI.Widgets;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class StorageBuffersSection
    {
        public static void Build(UIPanelBuilder builder, FacilitySnapshot snapshot)
        {
            if (snapshot.FacilityType == FacilityType.Depot)
            {
                return;
            }

            string sectionTitle = snapshot.FacilityType switch
            {
                FacilityType.CommercialFreight => "Storage Buffers",
                FacilityType.EngineService => "Locomotive Supplies & Reserves",
                FacilityType.MaintenanceOfWay => "Track Maintenance Material Reserves",
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, null)
            };

            string emptyMessage = snapshot.FacilityType switch
            {
                FacilityType.CommercialFreight => "<i>No storage buffers configured.</i>",
                FacilityType.EngineService => "<i>No locomotive supplies configured at this terminal.</i>",
                FacilityType.MaintenanceOfWay => "<i>No material reserves configured at this yard.</i>",
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, null)
            };

            BufferCardMode mode = snapshot.FacilityType switch
            {
                FacilityType.CommercialFreight => BufferCardMode.Commercial,
                FacilityType.EngineService => BufferCardMode.EngineSupply,
                FacilityType.MaintenanceOfWay => BufferCardMode.MowReserve,
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, null)
            };

            builder.AddSection(sectionTitle, b =>
            {
                if (snapshot.Buffers.Count == 0)
                {
                    b.AddLabel(emptyMessage);
                    return;
                }

                foreach (BufferInfo buffer in snapshot.Buffers)
                {
                    BufferCard.Render(b, buffer, mode);
                }
            });
        }
    }
}
