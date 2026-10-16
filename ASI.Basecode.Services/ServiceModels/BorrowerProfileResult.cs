namespace ASI.Basecode.Services.ServiceModels
{
    /// <summary>
    /// Outcome of a borrower-profile write. Mirrors UserAdministrationResult / ReservationResult
    /// so the controller can tell "saved" apart from "someone else changed this row" and
    /// "no such row" instead of collapsing everything into a bool.
    /// </summary>
    public sealed class BorrowerProfileResult
    {
        private BorrowerProfileResult(
            bool succeeded,
            bool stale,
            bool missing,
            string message)
        {
            Succeeded = succeeded;
            IsStale = stale;
            IsMissing = missing;
            Message = message;
        }

        public bool Succeeded { get; }
        public bool IsStale { get; }
        public bool IsMissing { get; }
        public string Message { get; }

        public static BorrowerProfileResult Success(string message) =>
            new BorrowerProfileResult(true, false, false, message);

        /// <summary>The form was rendered from an older version of the row.</summary>
        public static BorrowerProfileResult Stale(string message) =>
            new BorrowerProfileResult(false, true, false, message);

        public static BorrowerProfileResult Missing(string message) =>
            new BorrowerProfileResult(false, false, true, message);

        public static BorrowerProfileResult Failure(string message) =>
            new BorrowerProfileResult(false, false, false, message);
    }
}
