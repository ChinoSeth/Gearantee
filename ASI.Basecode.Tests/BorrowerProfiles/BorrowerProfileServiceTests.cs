using ASI.Basecode.Data;
using ASI.Basecode.Data.Models;
using ASI.Basecode.Services.Interfaces;
using ASI.Basecode.Services.ServiceModels;
using ASI.Basecode.Services.Services;
using ASI.Basecode.Tests.Dashboard;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace ASI.Basecode.Tests.BorrowerProfiles
{
    /// <summary>
    /// Regression tests for WBS 30.00–32.00. The service is exercised against the real
    /// production model on SQLite, the same way CategoryServiceTests does, and the write paths
    /// are asserted through AsNoTracking reloads so they prove persistence rather than
    /// in-memory mutation.
    /// </summary>
    public class BorrowerProfileServiceTests
    {
        [Fact]
        public async Task GetBorrowerProfilesAsync_ReturnsEveryProfileOrderedBySchoolId()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            await SeedProfileAsync(scope, "S-002", "Maria", "Santos", "College of Engineering", "0917-000-0002", isEligible: true);
            await SeedProfileAsync(scope, "S-001", "Juan", "Dela Cruz", "College of Computing", "0917-000-0001", isEligible: false);
            await SeedProfileAsync(scope, "S-003", "Ana", "Reyes", "College of Science", null, isEligible: true);

            var result = await service.GetBorrowerProfilesAsync(null, 1);
            var profiles = result.Borrowers;

            Assert.Equal(3, profiles.Count);
            Assert.Equal(new[] { "S-001", "S-002", "S-003" }, profiles.Select(profile => profile.SchoolId));

            Assert.Equal("Juan Dela Cruz", profiles[0].BorrowerName);
            Assert.Equal("College of Computing", profiles[0].Department);
            Assert.Equal("0917-000-0001", profiles[0].ContactNumber);
            Assert.False(profiles[0].IsEligible);

            Assert.True(profiles[1].IsEligible);
            Assert.Null(profiles[2].ContactNumber);

            // Every row carries the version token the edit form needs.
            Assert.All(profiles, profile => Assert.False(string.IsNullOrWhiteSpace(profile.ConcurrencyStamp)));
        }

        [Fact]
        public async Task GetBorrowerProfilesAsync_TrimsNameWhenLastNameIsBlank()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            await SeedProfileAsync(scope, "S-100", "Solo", string.Empty, "College of Arts", null, isEligible: true);

            var result = await service.GetBorrowerProfilesAsync(null, 1);
            var profile = Assert.Single(result.Borrowers);

            Assert.Equal("Solo", profile.BorrowerName);
        }

        [Fact]
        public async Task GetBorrowerProfileAsync_ReturnsTheMatchingProfileWithItsStamp()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-200", "Ken", "Aquino", "College of Business", "0917-200-2000", isEligible: false);

            var profile = await service.GetBorrowerProfileAsync(seeded.Profile.BorrowerProfileId);

            Assert.NotNull(profile);
            Assert.Equal("S-200", profile.SchoolId);
            Assert.Equal("Ken Aquino", profile.BorrowerName);
            Assert.Equal("College of Business", profile.Department);
            Assert.Equal("0917-200-2000", profile.ContactNumber);
            Assert.False(profile.IsEligible);
            Assert.Equal(seeded.Profile.ConcurrencyStamp, profile.ConcurrencyStamp);
        }

        [Fact]
        public async Task GetBorrowerProfileAsync_UnknownId_ReturnsNull()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            Assert.Null(await service.GetBorrowerProfileAsync(999_999));
        }

        [Fact]
        public async Task UpdateBorrowerProfileAsync_ChangesOnlyDepartmentAndContactNumberAndRotatesTheStamp()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-300", "Liza", "Mendoza", "College of Nursing", "0917-300-3000", isEligible: true);
            var stampBefore = seeded.Profile.ConcurrencyStamp;

            var originalStamp = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            seeded.Profile.CreatedAt = originalStamp;
            seeded.Profile.UpdatedAt = originalStamp;
            await db.SaveChangesAsync();

            var result = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "  College of Allied Health  ",
                    ContactNumber = "  0917-333-3333  ",
                    ConcurrencyStamp = stampBefore
                });

            Assert.True(result.Succeeded, result.Message);

            var stored = await db.BorrowerProfiles
                .AsNoTracking()
                .SingleAsync(item => item.BorrowerProfileId == seeded.Profile.BorrowerProfileId);

            Assert.Equal("College of Allied Health", stored.Department);
            Assert.Equal("0917-333-3333", stored.ContactNumber);

            // Everything the update form must never be able to reach.
            Assert.Equal("S-300", stored.SchoolId);
            Assert.Equal(seeded.User.Id, stored.UserId);
            Assert.True(stored.IsEligible);
            Assert.Equal(originalStamp, stored.CreatedAt);
            Assert.True(stored.UpdatedAt > originalStamp);

            // The version moves on so the form that was just saved cannot be replayed.
            Assert.NotEqual(stampBefore, stored.ConcurrencyStamp);
        }

        /// <summary>
        /// The regression test the review asked for: two forms open on the same row, the first
        /// saves, the second must be rejected rather than overwriting the first.
        /// </summary>
        [Fact]
        public async Task UpdateBorrowerProfileAsync_SecondStaleSubmissionIsRejectedAndTheFirstValueSurvives()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-350", "Rosa", "Lim", "College of Nursing", "0917-350-3500", isEligible: true);

            // Both administrators opened the edit form before either saved, so both hold the
            // same stamp — exactly what the rendered hidden field carries.
            var stampAsRendered = seeded.Profile.ConcurrencyStamp;
            var firstAdministrator = new BorrowerProfileUpdateViewModel
            {
                Department = "College of Nursing",
                ContactNumber = "0917-111-1111",
                ConcurrencyStamp = stampAsRendered
            };
            var secondAdministrator = new BorrowerProfileUpdateViewModel
            {
                Department = "College of Nursing",
                ContactNumber = "0917-999-9999",
                ConcurrencyStamp = stampAsRendered
            };

            var firstResult = await service.UpdateBorrowerProfileAsync(seeded.Profile.BorrowerProfileId, firstAdministrator);
            Assert.True(firstResult.Succeeded, firstResult.Message);

            var secondResult = await service.UpdateBorrowerProfileAsync(seeded.Profile.BorrowerProfileId, secondAdministrator);

            Assert.False(secondResult.Succeeded);
            Assert.True(secondResult.IsStale);
            Assert.Contains("changed", secondResult.Message, StringComparison.OrdinalIgnoreCase);

            var stored = await db.BorrowerProfiles
                .AsNoTracking()
                .SingleAsync(item => item.BorrowerProfileId == seeded.Profile.BorrowerProfileId);

            // The first save is intact — the stale form did not silently win.
            Assert.Equal("0917-111-1111", stored.ContactNumber);
        }

        [Fact]
        public async Task UpdateBorrowerProfileAsync_MissingStampIsRejected()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-360", "Ben", "Cruz", "College of Arts", "0917-360-3600", isEligible: true);

            var result = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "College of Arts",
                    ContactNumber = "0917-000-0000",
                    ConcurrencyStamp = null
                });

            Assert.True(result.IsStale);
        }

        [Fact]
        public async Task UpdateBorrowerProfileAsync_UnknownId_ReturnsMissingAndChangesNothing()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-400", "Noel", "Bautista", "College of Nursing", "0917-400-4000", isEligible: false);

            var result = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId + 5_000,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "Should not be written",
                    ContactNumber = "0999-999-9999",
                    ConcurrencyStamp = seeded.Profile.ConcurrencyStamp
                });

            Assert.True(result.IsMissing);

            var stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.Equal("College of Nursing", stored.Department);
            Assert.Equal("0917-400-4000", stored.ContactNumber);
        }

        [Fact]
        public async Task UpdateBorrowerProfileAsync_BlankDepartment_IsRejected()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-410", "Mila", "Ramos", "College of Arts", null, isEligible: true);

            var result = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "   ",
                    ContactNumber = null,
                    ConcurrencyStamp = seeded.Profile.ConcurrencyStamp
                });

            Assert.False(result.Succeeded);
            Assert.False(result.IsStale);
        }

        [Fact]
        public async Task UpdateEligibilityAsync_TogglesEligibilityWithoutTouchingProfileFields()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-500", "Grace", "Villamor", "College of Education", "0917-500-5000", isEligible: true);

            var result = await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, false);
            Assert.True(result.Succeeded, result.Message);

            var stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.False(stored.IsEligible);
            Assert.Equal("College of Education", stored.Department);
            Assert.Equal("0917-500-5000", stored.ContactNumber);
            Assert.Equal("S-500", stored.SchoolId);

            result = await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, true);
            Assert.True(result.Succeeded, result.Message);

            stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.True(stored.IsEligible);
        }

        [Fact]
        public async Task UpdateEligibilityAsync_AlreadyInRequestedState_IsANoOp()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-510", "Rico", "Diaz", "College of Arts", null, isEligible: true);
            var stampBefore = seeded.Profile.ConcurrencyStamp;

            var result = await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, true);

            Assert.True(result.Succeeded, result.Message);
            Assert.Contains("already", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(stampBefore, seeded.Profile.ConcurrencyStamp);
        }

        /// <summary>
        /// An eligibility change is a write to the row, so an edit form opened beforehand must
        /// not be able to save over it either.
        /// </summary>
        [Fact]
        public async Task UpdateEligibilityAsync_RotatesTheStampSoAnOpenEditFormIsRejected()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-520", "Ella", "Torres", "College of Nursing", "0917-520-5200", isEligible: true);

            var formOpenedBefore = new BorrowerProfileUpdateViewModel
            {
                Department = "College of Nursing",
                ContactNumber = "0917-000-0000",
                ConcurrencyStamp = seeded.Profile.ConcurrencyStamp
            };

            Assert.True((await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, false)).Succeeded);

            var result = await service.UpdateBorrowerProfileAsync(seeded.Profile.BorrowerProfileId, formOpenedBefore);

            Assert.True(result.IsStale);
        }

        [Fact]
        public async Task UpdateEligibilityAsync_UnknownId_ReturnsMissing()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var result = await service.UpdateEligibilityAsync(999_999, false);

            Assert.True(result.IsMissing);
        }

        [Fact]
        public void UpdateViewModel_RequiresDepartment()
        {
            var results = new List<ValidationResult>();
            var invalid = new BorrowerProfileUpdateViewModel
            {
                Department = null,
                ContactNumber = null
            };

            Assert.False(Validator.TryValidateObject(
                invalid,
                new ValidationContext(invalid),
                results,
                validateAllProperties: true));
            Assert.Contains(results, result => result.MemberNames.Contains(nameof(BorrowerProfileUpdateViewModel.Department)));

            results.Clear();
            var valid = new BorrowerProfileUpdateViewModel
            {
                Department = "College of Computing",
                ContactNumber = null
            };

            Assert.True(Validator.TryValidateObject(
                valid,
                new ValidationContext(valid),
                results,
                validateAllProperties: true));
        }

        [Fact]
        public void UpdateViewModel_EnforcesTheSqlColumnLengths()
        {
            var department = typeof(BorrowerProfileUpdateViewModel)
                .GetProperty(nameof(BorrowerProfileUpdateViewModel.Department))
                .GetCustomAttribute<StringLengthAttribute>();
            var contactNumber = typeof(BorrowerProfileUpdateViewModel)
                .GetProperty(nameof(BorrowerProfileUpdateViewModel.ContactNumber))
                .GetCustomAttribute<StringLengthAttribute>();

            Assert.NotNull(department);
            Assert.Equal(200, department.MaximumLength);
            Assert.NotNull(contactNumber);
            Assert.Equal(50, contactNumber.MaximumLength);
        }

        [Fact]
        public void UpdateViewModel_RejectsADepartmentLongerThanTheColumn()
        {
            var results = new List<ValidationResult>();
            var model = new BorrowerProfileUpdateViewModel
            {
                Department = new string('a', 201),
                ContactNumber = null
            };

            Assert.False(Validator.TryValidateObject(
                model,
                new ValidationContext(model),
                results,
                validateAllProperties: true));
            Assert.Contains(results, result => result.MemberNames.Contains(nameof(BorrowerProfileUpdateViewModel.Department)));

            results.Clear();
            model.Department = new string('a', 200);

            Assert.True(Validator.TryValidateObject(
                model,
                new ValidationContext(model),
                results,
                validateAllProperties: true));
        }

        [Fact]
        public async Task GetBorrowerProfilesAsync_PagesTenRecordsPerPage()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            for (var index = 1; index <= 12; index++)
            {
                await SeedProfileAsync(scope, $"S-{index:D3}", "Page", "Tester", "College of Computing", null, isEligible: true);
            }

            var firstPage = await service.GetBorrowerProfilesAsync(null, 1);

            Assert.Equal(12, firstPage.TotalCount);
            Assert.Equal(2, firstPage.TotalPages);
            Assert.Equal(1, firstPage.Page);
            Assert.Equal(10, firstPage.Borrowers.Count);
            Assert.Equal("S-001", firstPage.Borrowers[0].SchoolId);
            Assert.Equal("S-010", firstPage.Borrowers[9].SchoolId);

            var secondPage = await service.GetBorrowerProfilesAsync(null, 2);

            Assert.Equal(2, secondPage.Page);
            Assert.Equal(2, secondPage.Borrowers.Count);
            Assert.Equal("S-011", secondPage.Borrowers[0].SchoolId);
            Assert.Equal("S-012", secondPage.Borrowers[1].SchoolId);
        }

        [Fact]
        public async Task GetBorrowerProfilesAsync_ClampsAPagePastTheEnd()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            await SeedProfileAsync(scope, "S-700", "Only", "Three", "College of Arts", null, isEligible: true);
            await SeedProfileAsync(scope, "S-701", "Only", "Three", "College of Arts", null, isEligible: true);
            await SeedProfileAsync(scope, "S-702", "Only", "Three", "College of Arts", null, isEligible: true);

            var page = await service.GetBorrowerProfilesAsync(null, 99);

            Assert.Equal(1, page.TotalPages);
            Assert.Equal(1, page.Page);
            Assert.Equal(3, page.Borrowers.Count);
        }

        [Fact]
        public async Task GetBorrowerProfilesAsync_SearchesSchoolIdNameAndDepartment()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            await SeedProfileAsync(scope, "S-901", "Alpha", "Zamora", "College of Computing", null, isEligible: true);
            await SeedProfileAsync(scope, "S-902", "Beta", "Yap", "College of Nursing", null, isEligible: true);
            await SeedProfileAsync(scope, "S-903", "Gamma", "Xavier", "College of Arts", null, isEligible: true);

            var bySchoolId = await service.GetBorrowerProfilesAsync("s-902", 1);
            Assert.Equal("S-902", Assert.Single(bySchoolId.Borrowers).SchoolId);

            var byName = await service.GetBorrowerProfilesAsync("xavier", 1);
            Assert.Equal("S-903", Assert.Single(byName.Borrowers).SchoolId);

            var byDepartment = await service.GetBorrowerProfilesAsync("nursing", 1);
            Assert.Equal("S-902", Assert.Single(byDepartment.Borrowers).SchoolId);

            var noMatch = await service.GetBorrowerProfilesAsync("nothing matches this", 1);

            Assert.Empty(noMatch.Borrowers);
            Assert.Equal(0, noMatch.TotalCount);
            Assert.Equal(1, noMatch.TotalPages);
        }

        private static async Task<(ApplicationUser User, BorrowerProfile Profile)> SeedProfileAsync(
            AsyncServiceScope scope,
            string schoolId,
            string firstName,
            string lastName,
            string department,
            string contactNumber,
            bool isEligible)
        {
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await AddUserAsync(users, schoolId, firstName, lastName);

            var profile = new BorrowerProfile
            {
                UserId = user.Id,
                SchoolId = schoolId,
                Department = department,
                ContactNumber = contactNumber,
                IsEligible = isEligible,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.BorrowerProfiles.Add(profile);
            await db.SaveChangesAsync();

            return (user, profile);
        }

        private static async Task<ApplicationUser> AddUserAsync(
            UserManager<ApplicationUser> userManager,
            string schoolId,
            string firstName,
            string lastName)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var account = (schoolId + "-" + suffix).Replace(" ", string.Empty);

            var user = new ApplicationUser
            {
                UserName = account,
                UserCode = account,
                Email = account.ToLowerInvariant() + "@test.local",
                FirstName = firstName,
                LastName = lastName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, "S3cret!TestPassword");
            Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(error => error.Description)));

            return user;
        }

        private static ServiceProvider CreateProvider(SqliteDb database)
        {
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddDataProtection();
            services.AddDbContext<SqliteDashboardDbContext>(options =>
                options.UseSqlite(database.Connection));
            services.AddScoped<AsiBasecodeDBContext>(provider =>
                provider.GetRequiredService<SqliteDashboardDbContext>());
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<SqliteDashboardDbContext>()
                .AddDefaultTokenProviders();
            services.AddScoped<IBorrowerProfileService, BorrowerProfileService>();
            return services.BuildServiceProvider();
        }
    }
}
