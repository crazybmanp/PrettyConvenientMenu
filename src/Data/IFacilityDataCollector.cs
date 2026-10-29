using Model.Ops;
using PrettyConvenientMenu.Data.Facilities;

namespace PrettyConvenientMenu.Data
{
    public interface IFacilityDataCollector
    {
        IndustryFacilityType FacilityType { get; }
        void Collect(Industry industry, IndustrySnapshot snapshot);
    }
}
