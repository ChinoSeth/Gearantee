using System.ComponentModel.DataAnnotations;

namespace ASI.Basecode.Services.ServiceModels
{
    public class BorrowerProfileUpdateViewModel
    {
        [Required]
        public string Department { get; set; }

        [Phone]
        [Display(Name = "Contact number")]
        public string ContactNumber { get; set; }
    }
}