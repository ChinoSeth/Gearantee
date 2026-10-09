using ASI.Basecode.Services.ServiceModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ASI.Basecode.Services.Interfaces
{
    public interface IBorrowerProfileService
    {
        Task<List<BorrowerProfileViewModel>> GetBorrowerProfilesAsync();
        Task<BorrowerProfileViewModel> GetBorrowerProfileAsync(long id);
        Task<bool> UpdateBorrowerProfileAsync(long id, BorrowerProfileUpdateViewModel model);
        Task<bool> UpdateEligibilityAsync(long id, bool isEligible);
    }
}