using ASI.Basecode.Data;
using ASI.Basecode.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ASI.Basecode.Services.Services
{
    /// <summary>
    /// Provides the per-item SQL Server lock shared by inventory and reservation workflows.
    /// Call within a transaction, before checking reservation state or changing inventory.
    /// </summary>
    public static class EquipmentItemConcurrencyLock
    {
        public static EquipmentItem LoadForUpdate(
            AsiBasecodeDBContext db,
            long equipmentId,
            bool includeArchived = false)
        {
            EnsureSerializableTransaction(db);
            return BuildForUpdateQuery(db, equipmentId, includeArchived).SingleOrDefault();
        }

        public static async Task<EquipmentItem> LoadForUpdateAsync(
            AsiBasecodeDBContext db,
            long equipmentId,
            bool includeArchived = false)
        {
            EnsureSerializableTransaction(db);
            return await BuildForUpdateQuery(db, equipmentId, includeArchived).SingleOrDefaultAsync();
        }

        private static IQueryable<EquipmentItem> BuildForUpdateQuery(
            AsiBasecodeDBContext db,
            long equipmentId,
            bool includeArchived)
        {
            if (db.Database.IsSqlServer())
            {
                return includeArchived
                    ? db.EquipmentItems.FromSqlInterpolated($"""
                        SELECT *
                        FROM [EquipmentItem] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                        WHERE [EquipmentId] = {equipmentId}
                        """)
                    : db.EquipmentItems.FromSqlInterpolated($"""
                        SELECT *
                        FROM [EquipmentItem] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                        WHERE [EquipmentId] = {equipmentId} AND [IsArchived] = 0
                        """);
            }

            return db.EquipmentItems.Where(item =>
                item.EquipmentId == equipmentId && (includeArchived || !item.IsArchived));
        }

        private static void EnsureSerializableTransaction(AsiBasecodeDBContext db)
        {
            var transaction = db.Database.CurrentTransaction;
            if (transaction == null ||
                transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            {
                throw new InvalidOperationException(
                    "Equipment must be locked inside an active SERIALIZABLE transaction.");
            }
        }
    }
}
