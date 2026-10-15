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
using System.Threading.Tasks;
using Xunit;

namespace ASI.Basecode.Tests.BorrowerProfiles
{
    /// <summary>
    /// Regression tests for WBS 30.00 (view borrower's records) and WBS 31.00
    /// (update borrower's records). The service is deliberately exercised against the
    /// real production model on SQLite, the same way CategoryServiceTests does.
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

            var profiles = await service.GetBorrowerProfilesAsync();

            Assert.Equal(3, profiles.Count);
            Assert.Equal(new[] { "S-001", "S-002", "S-003" }, profiles.Select(profile => profile.SchoolId));

            Assert.Equal("Juan Dela Cruz", profiles[0].BorrowerName);
            Assert.Equal("College of Computing", profiles[0].Department);
            Assert.Equal("0917-000-0001", profiles[0].ContactNumber);
            Assert.False(profiles[0].IsEligible);

            Assert.True(profiles[1].IsEligible);
            Assert.Null(profiles[2].ContactNumber);
        }

        [Fact]
        public async Task GetBorrowerProfilesAsync_TrimsNameWhenLastNameIsBlank()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            await SeedProfileAsync(scope, "S-100", "Solo", string.Empty, "College of Arts", null, isEligible: true);

            var profile = Assert.Single(await service.GetBorrowerProfilesAsync());

            Assert.Equal("Solo", profile.BorrowerName);
        }

        [Fact]
        public async Task GetBorrowerProfileAsync_ReturnsTheMatchingProfile()
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
        public async Task UpdateBorrowerProfileAsync_ChangesOnlyDepartmentAndContactNumber()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-300", "Liza", "Mendoza", "College of Nursing", "0917-300-3000", isEligible: true);

            var originalStamp = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            seeded.Profile.CreatedAt = originalStamp;
            seeded.Profile.UpdatedAt = originalStamp;
            await db.SaveChangesAsync();

            var updated = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "College of Allied Health",
                    ContactNumber = "0917-333-3333"
                });

            Assert.True(updated);

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
        }

        [Fact]
        public async Task UpdateBorrowerProfileAsync_UnknownId_ReturnsFalseAndChangesNothing()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AsiBasecodeDBContext>();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            var seeded = await SeedProfileAsync(scope, "S-400", "Noel", "Bautista", "College of Nursing", "0917-400-4000", isEligible: false);

            var updated = await service.UpdateBorrowerProfileAsync(
                seeded.Profile.BorrowerProfileId + 5_000,
                new BorrowerProfileUpdateViewModel
                {
                    Department = "Should not be written",
                    ContactNumber = "0999-999-9999"
                });

            Assert.False(updated);

            var stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.Equal("College of Nursing", stored.Department);
            Assert.Equal("0917-400-4000", stored.ContactNumber);
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

            Assert.True(await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, false));

            var stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.False(stored.IsEligible);
            Assert.Equal("College of Education", stored.Department);
            Assert.Equal("0917-500-5000", stored.ContactNumber);
            Assert.Equal("S-500", stored.SchoolId);

            Assert.True(await service.UpdateEligibilityAsync(seeded.Profile.BorrowerProfileId, true));

            stored = await db.BorrowerProfiles.AsNoTracking().SingleAsync();
            Assert.True(stored.IsEligible);
        }

        [Fact]
        public async Task UpdateEligibilityAsync_UnknownId_ReturnsFalse()
        {
            using var database = new SqliteDb();
            using var provider = CreateProvider(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IBorrowerProfileService>();

            Assert.False(await service.UpdateEligibilityAsync(999_999, false));
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
