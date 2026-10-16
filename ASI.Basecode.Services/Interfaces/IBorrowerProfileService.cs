using ASI.Basecode.Services.ServiceModels;
using System.Threading.Tasks;

namespace ASI.Basecode.Services.Interfaces
{
    public interface IBorrowerProfileService
    {
        Task<BorrowerProfileListViewModel> GetBorrowerProfilesAsync(string search, int page);
        Task<BorrowerProfileViewModel> GetBorrowerProfileAsync(long id);
        Task<BorrowerProfileResult> UpdateBorrowerProfileAsync(long id, BorrowerProfileUpdateViewModel model);
        Task<BorrowerProfileResult> UpdateEligibilityAsync(long id, bool isEligible);
    }
}
