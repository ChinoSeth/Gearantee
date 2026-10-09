using ASI.Basecode.Data.Models;
using ASI.Basecode.Services.Interfaces;
using ASI.Basecode.Services.ServiceModels;
using ASI.Basecode.WebApp.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ASI.Basecode.WebApp.Controllers
{
    [Authorize(Policy = DomainValues.Permissions.BorrowerManage)]
    public class BorrowerProfileController : ControllerBase<BorrowerProfileController>
    {
        private readonly IBorrowerProfileService _borrowerProfileService;

        public BorrowerProfileController(
            IBorrowerProfileService borrowerProfileService,
            IHttpContextAccessor httpContextAccessor,
            ILoggerFactory loggerFactory,
            IConfiguration configuration)
            : base(httpContextAccessor, loggerFactory, configuration)
        {
            _borrowerProfileService = borrowerProfileService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var borrowers = await _borrowerProfileService.GetBorrowerProfilesAsync();
            return View(borrowers);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(long id)
        {
            var borrower = await _borrowerProfileService.GetBorrowerProfileAsync(id);
            if (borrower == null)
            {
                return NotFound();
            }

            ViewData["BorrowerProfileId"] = borrower.BorrowerProfileId;
            ViewData["SchoolId"] = borrower.SchoolId;
            ViewData["BorrowerName"] = borrower.BorrowerName;
            ViewData["IsEligible"] = borrower.IsEligible;

            var model = new BorrowerProfileUpdateViewModel
            {
                Department = borrower.Department,
                ContactNumber = borrower.ContactNumber
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(long id, BorrowerProfileUpdateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var borrower = await _borrowerProfileService.GetBorrowerProfileAsync(id);
                ViewData["BorrowerProfileId"] = id;
                ViewData["SchoolId"] = borrower?.SchoolId;
                ViewData["BorrowerName"] = borrower?.BorrowerName;
                ViewData["IsEligible"] = borrower?.IsEligible ?? false;
                return View(model);
            }

            var updated = await _borrowerProfileService.UpdateBorrowerProfileAsync(id, model);
            TempData[updated ? "SuccessMessage" : "ErrorMessage"] =
                updated ? "Borrower record updated successfully." : "Borrower record not found.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateEligibility(long id, bool isEligible)
        {
            var updated = await _borrowerProfileService.UpdateEligibilityAsync(id, isEligible);
            TempData[updated ? "SuccessMessage" : "ErrorMessage"] = updated
                ? (isEligible ? "Borrower marked as eligible." : "Borrower marked as ineligible.")
                : "Borrower record not found.";
            return RedirectToAction(nameof(Index));
        }
    }
}