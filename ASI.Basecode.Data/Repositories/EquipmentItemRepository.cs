using ASI.Basecode.Data.Interfaces;
using ASI.Basecode.Data.Models;
using Basecode.Data.Repositories;
using System.Linq;

namespace ASI.Basecode.Data.Repositories
{
    public class EquipmentItemRepository : BaseRepository, IEquipmentItemRepository
    {
        public EquipmentItemRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public IQueryable<EquipmentItem> GetEquipmentItems()
        {
            return this.GetDbSet<EquipmentItem>();
        }
    }
}
