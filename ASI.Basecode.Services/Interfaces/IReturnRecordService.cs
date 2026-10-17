using ASI.Basecode.Services.ServiceModels.Returns;

namespace ASI.Basecode.Services.Interfaces
{
    public interface IReturnRecordService
    {
        /// <summary>Tab is OnLoan, Overdue, Returned, or Late; empty means OnLoan.</summary>
        ReturnRecordIndexViewModel GetReturnRecords(string tab, int page);

        /// <summary>Returns null when the release doesn't exist or was already returned.</summary>
        ReturnRecordListItemViewModel RetrieveActiveLoan(long releaseRecordId);

        ReturnRecordResult AddReturnRecord(ReturnRecordViewModel model, string userId);

        /// <summary>Returns null when the return record doesn't exist.</summary>
        ReturnRecordDetailsViewModel RetrieveReturnRecord(long returnRecordId);
    }
}
