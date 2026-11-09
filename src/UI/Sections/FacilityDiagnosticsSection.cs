using PrettyConvenientMenu.Data.Facilities;
using UI.Builder;

namespace PrettyConvenientMenu.UI.Sections
{
    internal static class FacilityDiagnosticsSection
    {
        public static void Build(UIPanelBuilder builder, FacilitySnapshot snapshot)
        {
            if (snapshot.DiagnosticAlerts.Count > 0)
            {
                builder.AddSection("Advisory Notice", b =>
                {
                    for (int i = 0; i < snapshot.DiagnosticAlerts.Count; i++)
                    {
                        FacilityAdvisory alert = snapshot.DiagnosticAlerts[i];
                        string color = alert.OverrideColor ?? "#EF5350";
                        b.AddLabel($"<color={color}><b>Notice:</b> {alert.Message}</color>");
                    }
                });
            }
        }
    }
}
