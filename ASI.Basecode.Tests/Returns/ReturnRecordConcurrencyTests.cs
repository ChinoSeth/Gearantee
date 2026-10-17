using ASI.Basecode.Data;
using ASI.Basecode.Data.Models;
using ASI.Basecode.Services.ServiceModels.Returns;
using ASI.Basecode.Services.Utilities;
using ASI.Basecode.Tests.Dashboard;
using ASI.Basecode.Tests.UserAdministration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static ASI.Basecode.Tests.Reservations.ReservationTestHelpers;
using static ASI.Basecode.Tests.Returns.ReturnTestHelpers;

namespace ASI.Basecode.Tests.Returns
{
    /// <summary>Opt-in SQL Server races for returns; set GEARANTEE_TEST_SQLSERVER=1.</summary>
    public sealed class ReturnRecordConcurrencyTests
    {
        private static readonly DateTime Now = Utc(2026, 10, 17, 4);

        [SqlServerFact]
        public async Task SqlServer_concurrent_returns_of_one_loan_save_one_record()
        {
            using var database = new ReturnDatabase();
            string custodianId;
            long releaseId;
            using (var db = database.NewContext())
            {
                custodianId = Staff(db, "CUS-1", DomainValues.Roles.Custodian).Id;
                releaseId = Loan(db, "ITEM-1", Now.AddDays(-2), Now.AddHours(2), Now.AddDays(-2)).ReleaseRecordId;
            }

            var results = await Task.WhenAll(
                ReturnAsync(database, releaseId, custodianId),
                ReturnAsync(database, releaseId, custodianId));

            Assert.Single(results, result => result.Succeeded);
            Assert.Single(results, result => !result.Succeeded && result.Message.Contains("already been returned"));
            using var check = database.NewContext();
            Assert.Equal(1, check.ReturnRecords.Count());
        }

        [SqlServerFact]
        public async Task SqlServer_concurrent_returns_of_different_items_both_succeed()
        {
            using var database = new ReturnDatabase();
            string custodianId;
            long firstReleaseId, secondReleaseId;
            using (var db = database.NewContext())
            {
                custodianId = Staff(db, "CUS-1", DomainValues.Roles.Custodian).Id;
                firstReleaseId = Loan(db, "ITEM-1", Now.AddDays(-2), Now.AddHours(2), Now.AddDays(-2)).ReleaseRecordId;
                secondReleaseId = Loan(db, "ITEM-2", Now.AddDays(-2), Now.AddDays(-1), Now.AddDays(-2)).ReleaseRecordId;
            }

            var results = await Task.WhenAll(
                ReturnAsync(database, firstReleaseId, custodianId),
                ReturnAsync(database, secondReleaseId, custodianId));

            Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
            using var check = database.NewContext();
            Assert.Equal(2, check.ReturnRecords.Count());
            Assert.Equal(1, check.LateReturns.Count());
        }

        // Holds each INSERT back so both requests pass their checks first
        private static Task<ReturnRecordResult> ReturnAsync(ReturnDatabase database, long releaseRecordId, string userId)
        {
            return Task.Run(() =>
            {
                using var db = database.NewContext(new BeforeReturnInsert(() => Thread.Sleep(500)));
                return ReturnsFor(db, Now).AddReturnRecord(new ReturnRecordViewModel
                {
                    ReleaseRecordId = releaseRecordId,
                    ActualReturnAt = ManilaClock.ToLocal(Now),
                    ReturnedCondition = DomainValues.ReturnConditions.Good,
                    ResultingItemStatus = DomainValues.EquipmentStatuses.Available
                }, userId);
            });
        }

        private sealed class BeforeReturnInsert : DbCommandInterceptor
        {
            private readonly Action _action;
            private bool _fired;

            public BeforeReturnInsert(Action action) => _action = action;

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                if (!_fired && command.CommandText.Contains("INSERT INTO [ReturnRecord]", StringComparison.OrdinalIgnoreCase))
                {
                    _fired = true;
                    _action();
                }

                return result;
            }
        }

        // Uniquely named LocalDB database built from migrations, dropped afterwards
        private sealed class ReturnDatabase : IDisposable
        {
            private readonly string _connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=Gearantee_Returns_Test_" +
                Guid.NewGuid().ToString("N") + ";Trusted_Connection=True;TrustServerCertificate=True";

            public ReturnDatabase()
            {
                using var db = NewContext();
                db.Database.Migrate();
            }

            public AsiBasecodeDBContext NewContext(params IInterceptor[] interceptors)
            {
                return new AsiBasecodeDBContext(new DbContextOptionsBuilder<AsiBasecodeDBContext>()
                    .UseSqlServer(_connectionString)
                    .AddInterceptors(interceptors)
                    .Options);
            }

            public void Dispose()
            {
                using var db = NewContext();
                db.Database.EnsureDeleted();
            }
        }
    }
}
