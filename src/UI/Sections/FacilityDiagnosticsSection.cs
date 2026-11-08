using PrettyConvenientMenu.Data.Facilities;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class FacilityDiagnosticsSection
    {
        public static void Build(UIPanelBuilder builder, FacilitySnapshot snapshot)
        {
            if (!string.IsNullOrEmpty(snapshot.DiagnosticAlert))
            {
                builder.AddSection("Advisory Notice", b =>
                {
                    string color = snapshot.DiagnosticAlertColor ?? "#EF5350";
                    b.AddLabel($"<color={color}><b>Notice:</b> {snapshot.DiagnosticAlert}</color>");
                });
            }
        }
    }
}
