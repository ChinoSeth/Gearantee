using ASI.Basecode.Data;
using ASI.Basecode.Data.Models;
using ASI.Basecode.Services.Interfaces;
using ASI.Basecode.Services.ServiceModels;
using Microsoft.AspNetCore.Identity;
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
        private const string StaleMessage =
            "This borrower record changed after you opened it. Reload the page and try again.";

        private readonly AsiBasecodeDBContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public BorrowerProfileService(
            AsiBasecodeDBContext db,
            UserManager<ApplicationUser> userManager,
            ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<List<BorrowerProfileViewModel>> GetBorrowerProfilesAsync()
        {
            return await Project(_db.BorrowerProfiles.AsNoTracking())
                .OrderBy(profile => profile.SchoolId)
                .ToListAsync();
        }

        public async Task<BorrowerProfileViewModel> GetBorrowerProfileAsync(long id)
        {
            return await Project(
                    _db.BorrowerProfiles.AsNoTracking()
                        .Where(profile => profile.BorrowerProfileId == id))
                .SingleOrDefaultAsync();
        }

        public async Task<BorrowerProfileResult> UpdateBorrowerProfileAsync(
            long id,
            BorrowerProfileUpdateViewModel model)
        {
            if (model == null)
            {
                return BorrowerProfileResult.Failure(
                    "The update form was incomplete. Reload and try again.");
            }

            if (string.IsNullOrWhiteSpace(model.Department))
            {
                return BorrowerProfileResult.Failure("Department is required.");
            }

            var profile = await _db.BorrowerProfiles
                .SingleOrDefaultAsync(item => item.BorrowerProfileId == id);
            if (profile == null)
            {
                return BorrowerProfileResult.Missing("Borrower record not found.");
            }

            // The row is identified only by "id"; the stamp is what proves the posted form is
            // still current. Without this check, a form opened before another administrator
            // saved would silently overwrite the newer values.
            if (!IsCurrent(profile, model.ConcurrencyStamp))
            {
                return BorrowerProfileResult.Stale(StaleMessage);
            }

            profile.Department = model.Department.Trim();
            profile.ContactNumber = string.IsNullOrWhiteSpace(model.ContactNumber)
                ? null
                : model.ContactNumber.Trim();
            profile.ConcurrencyStamp = NewStamp();
            profile.UpdatedAt = DateTime.UtcNow;

            if (!await TrySaveAsync())
            {
                return BorrowerProfileResult.Stale(StaleMessage);
            }

            await RefreshSignInClaimsAsync(profile);
            return BorrowerProfileResult.Success("Borrower record updated successfully.");
        }

        public async Task<BorrowerProfileResult> UpdateEligibilityAsync(long id, bool isEligible)
        {
            var profile = await _db.BorrowerProfiles
                .SingleOrDefaultAsync(item => item.BorrowerProfileId == id);
            if (profile == null)
            {
                return BorrowerProfileResult.Missing("Borrower record not found.");
            }

            // A raced double-toggle cannot apply twice: the second submission sees the value
            // already in place and reports that instead of writing again.
            if (profile.IsEligible == isEligible)
            {
                return BorrowerProfileResult.Success(
                    isEligible
                        ? "Borrower is already marked eligible."
                        : "Borrower is already marked ineligible.");
            }

            profile.IsEligible = isEligible;
            profile.ConcurrencyStamp = NewStamp();
            profile.UpdatedAt = DateTime.UtcNow;

            if (!await TrySaveAsync())
            {
                return BorrowerProfileResult.Stale(StaleMessage);
            }

            return BorrowerProfileResult.Success(
                isEligible
                    ? "Borrower marked as eligible."
                    : "Borrower marked as ineligible.");
        }

        private static IQueryable<BorrowerProfileViewModel> Project(
            IQueryable<BorrowerProfile> profiles)
        {
            return profiles.Select(profile => new BorrowerProfileViewModel
            {
                BorrowerProfileId = profile.BorrowerProfileId,
                SchoolId = profile.SchoolId,
                BorrowerName = (profile.User.FirstName + " " + profile.User.LastName).Trim(),
                Department = profile.Department,
                ContactNumber = profile.ContactNumber,
                IsEligible = profile.IsEligible,
                ConcurrencyStamp = profile.ConcurrencyStamp
            });
        }

        private static bool IsCurrent(BorrowerProfile profile, string postedStamp)
        {
            return !string.IsNullOrEmpty(postedStamp) &&
                string.Equals(profile.ConcurrencyStamp, postedStamp, StringComparison.Ordinal);
        }

        private static string NewStamp() => Guid.NewGuid().ToString();

        private async Task<bool> TrySaveAsync()
        {
            try
            {
                await _db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Two administrators saved in the same instant: the token is the tie-breaker.
                return false;
            }
        }

        /// <summary>
        /// Department is snapshotted into the sign-in claims and rendered in the sidebar, so an
        /// already-signed-in borrower would keep seeing the previous value. Refreshing the
        /// security stamp makes the principal rebuild (within the configured validation
        /// interval) from current claim values. This mirrors the account-edit flow in
        /// UserAdministrationService.
        /// </summary>
        private async Task RefreshSignInClaimsAsync(BorrowerProfile profile)
        {
            if (string.IsNullOrEmpty(profile.UserId))
            {
                return;
            }

            var user = await _db.Users
                .SingleOrDefaultAsync(item => item.Id == profile.UserId);
            if (user == null)
            {
                return;
            }

            await _userManager.UpdateSecurityStampAsync(user);
        }
    }
}
