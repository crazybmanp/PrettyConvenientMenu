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
                builder.AddSection("Advisories", b =>
                {
                    for (int i = 0; i < snapshot.DiagnosticAlerts.Count; i++)
                    {
                        FacilityAdvisory alert = snapshot.DiagnosticAlerts[i];
                        (string tag, string color) = alert.Level switch
                        {
                            AdvisoryLevel.Hold => ("Hold", "#EF5350"),
                            AdvisoryLevel.Caution => ("Caution", "#FFD54F"),
                            _ => ("Notice", "#90CAF9")
                        };
                        b.AddLabel($"<color={color}><b>{tag}:</b> {alert.Message}</color>");
                    }
                });
            }
        }
    }
}
