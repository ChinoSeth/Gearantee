namespace ASI.Basecode.Services.ServiceModels.Reservations
{
    /// <summary>Outcome of a borrower action on a reservation (submit, cancel).</summary>
    public class ReservationResult
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = string.Empty;
        public long? ReservationId { get; set; }

        /// <summary>True when the reservation doesn't exist or isn't the caller's; the controller returns 404.</summary>
        public bool NotFound { get; set; }

        public static ReservationResult Fail(string message) =>
            new ReservationResult { Message = message };

        public static ReservationResult Missing() =>
            new ReservationResult { NotFound = true, Message = "Reservation not found." };
    }
}
