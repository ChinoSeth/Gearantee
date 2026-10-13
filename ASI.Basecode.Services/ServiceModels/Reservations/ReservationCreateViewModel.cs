using ASI.Basecode.Services.ServiceModels.Catalog;
using System;
using System.ComponentModel.DataAnnotations;

namespace ASI.Basecode.Services.ServiceModels.Reservations
{
    public class ReservationCreateViewModel
    {
        [Required]
        public long EquipmentId { get; set; }

        /// <summary>Manila local time, as entered in a datetime-local input.</summary>
        [Required(ErrorMessage = "Enter the start date and time.")]
        [DataType(DataType.DateTime)]
        [Display(Name = "Start")]
        public DateTime? ReservationStart { get; set; }

        /// <summary>Manila local time, as entered in a datetime-local input.</summary>
        [Required(ErrorMessage = "Enter the end date and time.")]
        [DataType(DataType.DateTime)]
        [Display(Name = "End")]
        public DateTime? ReservationEnd { get; set; }

        [Required(ErrorMessage = "Tell the custodian what the equipment is for.")]
        [StringLength(1000)]
        [Display(Name = "Purpose")]
        public string Purpose { get; set; } = string.Empty;

        /// <summary>Item summary and booked times for display only; never read from the form.</summary>
        public CatalogItemDetailsViewModel Item { get; set; }
    }
}
