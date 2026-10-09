namespace ASI.Basecode.Services.ServiceModels
{
    public class BorrowerProfileViewModel
    {
        public long BorrowerProfileId { get; set; }
        public string SchoolId { get; set; }
        public string BorrowerName { get; set; }
        public string Department { get; set; }
        public string ContactNumber { get; set; }
        public bool IsEligible { get; set; }
    }
}