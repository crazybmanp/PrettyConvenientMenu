using System;
using PrettyConvenientMenu.Data.Buffers;
using PrettyConvenientMenu.Data.Facilities;
using PrettyConvenientMenu.UI.Widgets;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class StorageBuffersSection
    {
        public static void Build(UIPanelBuilder builder, IndustrySnapshot snapshot)
        {
            if (snapshot.FacilityType == IndustryFacilityType.Depot)
            {
                return;
            }

            string sectionTitle = snapshot.FacilityType switch
            {
                IndustryFacilityType.CommercialFreight => "Storage Buffers",
                IndustryFacilityType.EngineService => "Locomotive Supplies & Reserves",
                IndustryFacilityType.MaintenanceOfWay => "Track Maintenance Material Reserves",
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, null)
            };

            string emptyMessage = snapshot.FacilityType switch
            {
                IndustryFacilityType.CommercialFreight => "<i>No storage buffers configured.</i>",
                IndustryFacilityType.EngineService => "<i>No locomotive supplies configured at this terminal.</i>",
                IndustryFacilityType.MaintenanceOfWay => "<i>No material reserves configured at this yard.</i>",
                _ => throw new ArgumentOutOfRangeException(nameof(snapshot.FacilityType), snapshot.FacilityType, null)
            };

            BufferCardMode mode = snapshot.FacilityType switch
            {
                IndustryFacilityType.CommercialFreight => BufferCardMode.Commercial,
                IndustryFacilityType.EngineService => BufferCardMode.EngineSupply,
                IndustryFacilityType.MaintenanceOfWay => BufferCardMode.MowReserve,
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
