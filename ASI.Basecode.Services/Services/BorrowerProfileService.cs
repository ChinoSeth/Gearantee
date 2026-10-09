using ASI.Basecode.Data;
using ASI.Basecode.Services.Interfaces;
using ASI.Basecode.Services.ServiceModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ASI.Basecode.Services.Services
{
    public class BorrowerProfileService : ServiceBase, IBorrowerProfileService
    {
        private readonly AsiBasecodeDBContext _db;

        public BorrowerProfileService(AsiBasecodeDBContext db, ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
            _db = db;
        }

        public async Task<List<BorrowerProfileViewModel>> GetBorrowerProfilesAsync()
        {
            return await _db.BorrowerProfiles
                .Include(profile => profile.User)
                .OrderBy(profile => profile.SchoolId)
                .Select(profile => new BorrowerProfileViewModel
                {
                    BorrowerProfileId = profile.BorrowerProfileId,
                    SchoolId = profile.SchoolId,
                    BorrowerName = (profile.User.FirstName + " " + profile.User.LastName).Trim(),
                    Department = profile.Department,
                    ContactNumber = profile.ContactNumber,
                    IsEligible = profile.IsEligible
                })
                .ToListAsync();
        }

        public async Task<BorrowerProfileViewModel> GetBorrowerProfileAsync(long id)
        {
            return await _db.BorrowerProfiles
                .Include(profile => profile.User)
                .Where(profile => profile.BorrowerProfileId == id)
                .Select(profile => new BorrowerProfileViewModel
                {
                    BorrowerProfileId = profile.BorrowerProfileId,
                    SchoolId = profile.SchoolId,
                    BorrowerName = (profile.User.FirstName + " " + profile.User.LastName).Trim(),
                    Department = profile.Department,
                    ContactNumber = profile.ContactNumber,
                    IsEligible = profile.IsEligible
                })
                .SingleOrDefaultAsync();
        }

        public async Task<bool> UpdateBorrowerProfileAsync(long id, BorrowerProfileUpdateViewModel model)
        {
            var profile = await _db.BorrowerProfiles
                .SingleOrDefaultAsync(item => item.BorrowerProfileId == id);
            if (profile == null)
            {
                return false;
            }

            // The target record is identified by the separate "id" parameter — never by
            // anything in the posted body. The model itself can only ever carry
            // Department and ContactNumber.
            profile.Department = model.Department;
            profile.ContactNumber = model.ContactNumber;
            profile.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateEligibilityAsync(long id, bool isEligible)
        {
            var profile = await _db.BorrowerProfiles
                .SingleOrDefaultAsync(item => item.BorrowerProfileId == id);
            if (profile == null)
            {
                return false;
            }

            profile.IsEligible = isEligible;
            profile.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return true;
        }
    }
}