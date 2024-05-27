
namespace lk.Server.GroupPaymentGateway.Models.DBModels
{
    public partial class DbM_SystemConfigs 
    {
        public enum EnviromentType { Development = 0, Production = 1 }

        
        public int Id { get; set; }
        public string PaymentGWayApiKey { get; set; }
        public string PaymentGWayEndpoint { get; set; }
        public string PostbackUrl { get; set; }
        public string SendgridApi { get; set; }
        public string SMSUserName { get; set; }
        public string SMSPassword { get; set; }
        public string NotificationsSchedulerId { get; set; }
        public string ChargesUpdaterSchedulerId { get; set; }

        public EnviromentType Environment { get; set; }
    }


    public partial class DbM_UserChargePlan
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Amount { get; set; }


        public virtual DbM_User UserNavigation { get; set; }       
        public virtual List<DbM_UserChargePlanNotificationSettings> NotificationsSettings { get; set; }
    }
}
