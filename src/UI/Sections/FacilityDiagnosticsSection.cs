using PrettyConvenientMenu.Data.Facilities;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class FacilityDiagnosticsSection
    {
        public static void Build(UIPanelBuilder builder, IndustrySnapshot snapshot)
        {
            if (!string.IsNullOrEmpty(snapshot.DiagnosticAlert))
            {
                builder.AddSection("Advisory Notice", b =>
                {
                    b.AddLabel($"<color=#EF5350><b>Notice:</b> {snapshot.DiagnosticAlert}</color>");
                });
            }
        }
    }
}
